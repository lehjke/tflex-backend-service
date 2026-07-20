using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Infrastructure.Configuration;

namespace TFlexDrawingService.Infrastructure.Automation;

public interface ITFlexAutomationHealthProbe
{
    Task<TFlexAutomationHealthResult> CheckAsync(
        CancellationToken cancellationToken = default);
}

public sealed record TFlexAutomationHealthResult(bool Ready, string Detail)
{
    public static TFlexAutomationHealthResult Pass(string detail)
    {
        return new TFlexAutomationHealthResult(true, detail);
    }

    public static TFlexAutomationHealthResult Fail(string detail)
    {
        return new TFlexAutomationHealthResult(false, detail);
    }
}

public sealed class TFlexAutomationExecutionGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public Task WaitAsync(CancellationToken cancellationToken)
    {
        return _semaphore.WaitAsync(cancellationToken);
    }

    public void Release()
    {
        _semaphore.Release();
    }
}

public sealed class TFlexAutomationReadinessState
{
    private readonly object _gate = new();
    private TFlexAutomationReadinessSnapshot _snapshot =
        TFlexAutomationReadinessSnapshot.NotChecked;
    private TaskCompletionSource _readySignal = CreateSignal();

    public TFlexAutomationReadinessSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return _snapshot.Ready
                && _snapshot.ValidUntil is { } validUntil
                && validUntil >= DateTimeOffset.UtcNow
                    ? _snapshot
                    : _snapshot with { Ready = false };
        }
    }

    public void Update(
        TFlexAutomationHealthResult result,
        DateTimeOffset checkedAt,
        TimeSpan validity)
    {
        lock (_gate)
        {
            _snapshot = new TFlexAutomationReadinessSnapshot(
                result.Ready,
                checkedAt,
                result.Ready ? checkedAt + validity : checkedAt);
            if (result.Ready)
            {
                _readySignal.TrySetResult();
            }
            else if (_readySignal.Task.IsCompleted)
            {
                _readySignal = CreateSignal();
            }
        }
    }

    public async Task WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task readyTask;
            lock (_gate)
            {
                if (_snapshot.Ready
                    && _snapshot.ValidUntil is { } validUntil
                    && validUntil >= DateTimeOffset.UtcNow)
                {
                    return;
                }

                if (_readySignal.Task.IsCompleted)
                {
                    _readySignal = CreateSignal();
                }

                readyTask = _readySignal.Task;
            }

            await readyTask.WaitAsync(cancellationToken);
        }
    }

    private static TaskCompletionSource CreateSignal()
    {
        return new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

public sealed record TFlexAutomationReadinessSnapshot(
    bool Ready,
    DateTimeOffset? CheckedAt,
    DateTimeOffset? ValidUntil)
{
    public static TFlexAutomationReadinessSnapshot NotChecked { get; } =
        new(false, null, null);
}

public sealed class TFlexAutomationHealthProbe(
    IOptions<TFlexAutomationOptions> options,
    TFlexAutomationExecutionGate executionGate,
    TFlexAutomationReadinessState readinessState,
    ILogger<TFlexAutomationHealthProbe> logger) : ITFlexAutomationHealthProbe
{
    private readonly TFlexAutomationOptions _options = options.Value;

    public async Task<TFlexAutomationHealthResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        var checkedAt = DateTimeOffset.UtcNow;
        if (!_options.HealthCheckEnabled)
        {
            return Publish(
                TFlexAutomationHealthResult.Fail(
                    "Automation health-check is disabled."),
                checkedAt);
        }

        var mode = (_options.Mode ?? string.Empty).Trim();
        if (string.Equals(mode, "mock", StringComparison.OrdinalIgnoreCase))
        {
            return Publish(
                TFlexAutomationHealthResult.Pass("Mock automation is available."),
                checkedAt);
        }

        if (!IsExternalMode(mode))
        {
            return Publish(
                TFlexAutomationHealthResult.Fail(
                    $"Unsupported automation mode '{mode}'."),
                checkedAt);
        }

        if (string.IsNullOrWhiteSpace(_options.CommandPath)
            || !File.Exists(_options.CommandPath))
        {
            return Publish(
                TFlexAutomationHealthResult.Fail(
                    "External automation runner was not found."),
                checkedAt);
        }

        await executionGate.WaitAsync(cancellationToken);
        try
        {
            return Publish(
                await RunExternalHealthCheckAsync(cancellationToken),
                checkedAt);
        }
        finally
        {
            executionGate.Release();
        }
    }

    private TFlexAutomationHealthResult Publish(
        TFlexAutomationHealthResult result,
        DateTimeOffset checkedAt)
    {
        readinessState.Update(
            result,
            checkedAt,
            GetHealthValidity(_options));
        return result;
    }

    private static TimeSpan GetHealthValidity(TFlexAutomationOptions options)
    {
        var interval = TimeSpan.FromSeconds(
            Math.Clamp(options.HealthCheckIntervalSeconds, 5, 86_400));
        var timeout = TimeSpan.FromSeconds(
            Math.Clamp(options.HealthCheckTimeoutSeconds, 1, 3_600));
        return interval + interval + timeout;
    }

    private async Task<TFlexAutomationHealthResult> RunExternalHealthCheckAsync(
        CancellationToken cancellationToken)
    {
        var commandPath = Path.GetFullPath(_options.CommandPath!);
        var startInfo = new ProcessStartInfo
        {
            FileName = commandPath,
            WorkingDirectory = Path.GetDirectoryName(commandPath)
                ?? Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--health-check");

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return TFlexAutomationHealthResult.Fail(
                    "External automation health-check could not be started.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var timeout = TimeSpan.FromSeconds(
                Math.Max(1, _options.HealthCheckTimeoutSeconds));
            try
            {
                await process.WaitForExitAsync(cancellationToken)
                    .WaitAsync(timeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                await TerminateProcessTreeAsync(process);
                return TFlexAutomationHealthResult.Fail(
                    $"External automation health-check timed out after {timeout.TotalSeconds:0}s.");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (process.ExitCode == 0)
            {
                if (!string.IsNullOrWhiteSpace(stdout))
                {
                    logger.LogInformation(
                        "T-FLEX automation health-check output: {Output}",
                        Truncate(stdout));
                }

                return TFlexAutomationHealthResult.Pass(
                    "External automation session opened and closed successfully.");
            }

            logger.LogWarning(
                "T-FLEX automation health-check failed with exit code {ExitCode}: {Error}",
                process.ExitCode,
                Truncate(stderr));
            return TFlexAutomationHealthResult.Fail(
                $"External automation health-check exited with code {process.ExitCode}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TerminateProcessTreeAsync(process);
            throw;
        }
        catch (Exception exception)
        {
            await TerminateProcessTreeAsync(process);
            logger.LogWarning(
                exception,
                "T-FLEX automation health-check could not be executed.");
            return TFlexAutomationHealthResult.Fail(
                "External automation health-check could not be executed.");
        }
    }

    private static bool IsExternalMode(string mode)
    {
        return mode.Equals("external", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("externalprocess", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("real", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task TerminateProcessTreeAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
        catch
        {
            // Preserve the health-check result; process cleanup is best effort.
        }
    }

    private static string Truncate(string value)
    {
        const int maxLength = 2048;
        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength] + "…";
    }
}
