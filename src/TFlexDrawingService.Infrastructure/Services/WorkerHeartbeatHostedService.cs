using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Infrastructure.Automation;
using TFlexDrawingService.Infrastructure.Configuration;

namespace TFlexDrawingService.Infrastructure.Services;

public sealed class WorkerHeartbeatHostedService(
    IOptions<DrawingStorageOptions> storageOptions,
    IOptions<TFlexAutomationOptions> automationOptions,
    ITFlexAutomationHealthProbe healthProbe,
    TFlexAutomationReadinessState readinessState,
    ILogger<WorkerHeartbeatHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StaleFileRetention = TimeSpan.FromDays(1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly DrawingStorageOptions _storageOptions = storageOptions.Value;
    private readonly TFlexAutomationOptions _automationOptions = automationOptions.Value;
    private readonly SemaphoreSlim _heartbeatWriteGate = new(1, 1);
    private string? _heartbeatPath;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var heartbeatDirectory = Path.Combine(
            _storageOptions.RootPath,
            "worker-heartbeats");
        Directory.CreateDirectory(heartbeatDirectory);
        _heartbeatPath = Path.Combine(
            heartbeatDirectory,
            $"worker-{Environment.ProcessId}.json");
        CleanupStaleFiles(heartbeatDirectory);

        try
        {
            await Task.WhenAll(
                RunHeartbeatLoopAsync(stoppingToken),
                RunAutomationHealthLoopAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal service shutdown.
        }
        finally
        {
            TryDeleteHeartbeat();
        }
    }

    private async Task RunHeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        await WriteHeartbeatAsync(cancellationToken);
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            await WriteHeartbeatAsync(cancellationToken);
        }
    }

    private async Task RunAutomationHealthLoopAsync(CancellationToken cancellationToken)
    {
        if (!_automationOptions.HealthCheckEnabled)
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(
            Math.Clamp(_automationOptions.HealthCheckIntervalSeconds, 5, 86_400));
        var validity = TFlexAutomationStartupHostedService.GetHealthValidity(
            _automationOptions);

        if (readinessState.GetSnapshot().CheckedAt is not null)
        {
            await Task.Delay(interval, cancellationToken);
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var checkedAt = DateTimeOffset.UtcNow;
            TFlexAutomationHealthResult result;
            try
            {
                result = await healthProbe.CheckAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Unexpected error while checking T-FLEX automation readiness.");
                result = TFlexAutomationHealthResult.Fail(
                    "Automation health-check failed unexpectedly.");
            }

            readinessState.Update(result, checkedAt, validity);

            await WriteHeartbeatAsync(cancellationToken);
            await Task.Delay(interval, cancellationToken);
        }
    }

    private async Task WriteHeartbeatAsync(CancellationToken cancellationToken)
    {
        await _heartbeatWriteGate.WaitAsync(cancellationToken);
        try
        {
            if (_heartbeatPath is null)
            {
                return;
            }

            var mode = (_automationOptions.Mode ?? string.Empty).Trim();
            var healthState = readinessState.GetSnapshot();

            var heartbeat = new WorkerHeartbeatDocument(
                healthState.Ready,
                Environment.ProcessId,
                DateTimeOffset.UtcNow,
                mode,
                healthState.CheckedAt,
                healthState.ValidUntil);
            var temporaryPath = _heartbeatPath + ".tmp";

            try
            {
                await using (var stream = new FileStream(
                                 temporaryPath,
                                 FileMode.Create,
                                 FileAccess.Write,
                                 FileShare.None,
                                 bufferSize: 4096,
                                 FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(
                        stream,
                        heartbeat,
                        JsonOptions,
                        cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }

                File.Move(temporaryPath, _heartbeatPath, overwrite: true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to publish Worker readiness heartbeat.");
            }
            finally
            {
                TryDeleteFile(temporaryPath);
            }
        }
        finally
        {
            _heartbeatWriteGate.Release();
        }
    }

    private static void CleanupStaleFiles(string directory)
    {
        var cutoff = DateTimeOffset.UtcNow - StaleFileRetention;
        foreach (var path in Directory.EnumerateFiles(
                     directory,
                     "worker-*.json*",
                     SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff.UtcDateTime)
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Another Worker may own or replace the file.
            }
        }
    }

    private void TryDeleteHeartbeat()
    {
        if (_heartbeatPath is not null)
        {
            TryDeleteFile(_heartbeatPath);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Stale heartbeat files expire and do not affect readiness.
        }
    }

    private sealed record WorkerHeartbeatDocument(
        bool Ready,
        int ProcessId,
        DateTimeOffset UpdatedAt,
        string AutomationMode,
        DateTimeOffset? AutomationCheckedAt,
        DateTimeOffset? AutomationHealthValidUntil);
}
