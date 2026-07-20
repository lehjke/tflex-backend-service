using System.Text.Json;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Core.Abstractions;
using TFlexDrawingService.Infrastructure.Configuration;

namespace TFlexDrawingService.Api.Data;

public sealed class ServiceReadinessProbe(
    ITemplateCatalog catalog,
    IDrawingJobRepository jobs,
    IOptions<DrawingStorageOptions> storageOptions)
{
    public const string WorkerHeartbeatDirectoryName = "worker-heartbeats";
    public static readonly TimeSpan MaximumWorkerHeartbeatAge = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly DrawingStorageOptions _storageOptions = storageOptions.Value;

    public async Task<ServiceReadinessResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        var checks = new Dictionary<string, ReadinessCheck>(StringComparer.OrdinalIgnoreCase);

        await CheckTemplatesAsync(checks, cancellationToken);
        await CheckDatabaseAsync(checks, cancellationToken);
        await CheckWorkerHeartbeatAsync(checks, cancellationToken);

        return new ServiceReadinessResult(
            checks.Values.All(check => check.Ready),
            DateTimeOffset.UtcNow,
            checks);
    }

    private async Task CheckTemplatesAsync(
        IDictionary<string, ReadinessCheck> checks,
        CancellationToken cancellationToken)
    {
        try
        {
            var templates = await catalog.ListAsync(cancellationToken);
            var missing = templates
                .Where(template => !File.Exists(template.TemplateFilePath))
                .Select(template => template.Code)
                .Take(10)
                .ToArray();
            checks["templates"] = templates.Count > 0 && missing.Length == 0
                ? ReadinessCheck.Pass($"{templates.Count} template(s) loaded.")
                : ReadinessCheck.Fail(
                    templates.Count == 0
                        ? "Template catalog is empty."
                        : $"Missing template files: {string.Join(", ", missing)}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            checks["templates"] = ReadinessCheck.Fail("Template catalog could not be loaded.");
        }
    }

    private async Task CheckDatabaseAsync(
        IDictionary<string, ReadinessCheck> checks,
        CancellationToken cancellationToken)
    {
        try
        {
            var activeJobs = await jobs.CountActiveAsync(cancellationToken: cancellationToken);
            checks["database"] = ReadinessCheck.Pass(
                $"Drawing database is reachable; {activeJobs} active job(s).");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            checks["database"] = ReadinessCheck.Fail("Drawing database is not reachable.");
        }
    }

    private async Task CheckWorkerHeartbeatAsync(
        IDictionary<string, ReadinessCheck> checks,
        CancellationToken cancellationToken)
    {
        var heartbeatDirectory = Path.Combine(
            _storageOptions.RootPath,
            WorkerHeartbeatDirectoryName);
        if (!Directory.Exists(heartbeatDirectory))
        {
            checks["worker"] = ReadinessCheck.Fail("Worker heartbeat directory was not found.");
            return;
        }

        try
        {
            var heartbeats = new List<WorkerHeartbeat>();
            foreach (var heartbeatPath in Directory.EnumerateFiles(
                         heartbeatDirectory,
                         "*.json",
                         SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await using var stream = new FileStream(
                        heartbeatPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete,
                        bufferSize: 4096,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    var parsedHeartbeat = await JsonSerializer.DeserializeAsync<WorkerHeartbeat>(
                        stream,
                        JsonOptions,
                        cancellationToken);
                    if (parsedHeartbeat is not null)
                    {
                        heartbeats.Add(parsedHeartbeat);
                    }
                }
                catch (IOException)
                {
                    // A worker may be atomically replacing its heartbeat.
                }
                catch (JsonException)
                {
                    // Ignore an incomplete or stale heartbeat and keep looking.
                }
            }

            var now = DateTimeOffset.UtcNow;
            var heartbeat = heartbeats
                .Where(candidate =>
                    candidate.Ready
                    && now - candidate.UpdatedAt >= TimeSpan.Zero
                    && now - candidate.UpdatedAt <= MaximumWorkerHeartbeatAge
                    && candidate.AutomationCheckedAt is { } checkedAt
                    && candidate.AutomationHealthValidUntil is { } validUntil
                    && now - checkedAt >= TimeSpan.Zero
                    && validUntil >= now
                    && validUntil >= checkedAt
                    && validUntil - checkedAt <= TimeSpan.FromDays(3))
                .OrderByDescending(candidate => candidate.UpdatedAt)
                .FirstOrDefault();
            if (heartbeat is null)
            {
                checks["worker"] = ReadinessCheck.Fail(
                    "No current ready Worker heartbeat was found.");
                return;
            }

            var age = now - heartbeat.UpdatedAt;
            var automationHealthAge = now - heartbeat.AutomationCheckedAt!.Value;
            var automationMode = string.Equals(
                heartbeat.AutomationMode,
                "mock",
                StringComparison.OrdinalIgnoreCase)
                ? "Mock"
                : "ExternalProcess";
            checks["worker"] = ReadinessCheck.Pass(
                $"Ready Worker heartbeat age {Math.Round(age.TotalSeconds)}s "
                + $"({automationMode}; automation checked "
                + $"{Math.Round(automationHealthAge.TotalSeconds)}s ago).");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            checks["worker"] = ReadinessCheck.Fail("Worker heartbeat could not be inspected.");
        }
    }
}

public sealed record ServiceReadinessResult(
    bool Ready,
    DateTimeOffset Time,
    IReadOnlyDictionary<string, ReadinessCheck> Checks);

public sealed record ReadinessCheck(bool Ready, string Detail)
{
    public static ReadinessCheck Pass(string detail)
    {
        return new ReadinessCheck(true, detail);
    }

    public static ReadinessCheck Fail(string detail)
    {
        return new ReadinessCheck(false, detail);
    }
}

public sealed record WorkerHeartbeat(
    bool Ready,
    int ProcessId,
    DateTimeOffset UpdatedAt,
    string AutomationMode,
    DateTimeOffset? AutomationCheckedAt = null,
    DateTimeOffset? AutomationHealthValidUntil = null);
