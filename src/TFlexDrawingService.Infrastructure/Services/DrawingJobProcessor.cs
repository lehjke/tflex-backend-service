using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Core.Abstractions;
using TFlexDrawingService.Core.Models;
using TFlexDrawingService.Infrastructure.Automation;
using TFlexDrawingService.Infrastructure.Configuration;

namespace TFlexDrawingService.Infrastructure.Services;

public sealed class DrawingJobProcessor(
    ITemplateCatalog templateCatalog,
    IDrawingJobRepository repository,
    IFileStorage fileStorage,
    ITFlexAutomationClient automationClient,
    TFlexAutomationReadinessState automationReadiness,
    IOptions<DrawingQueueOptions> queueOptions,
    ILogger<DrawingJobProcessor> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task ProcessAsync(DrawingJob job, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(job.LeaseToken) || job.LeaseExpiresAt is null)
        {
            throw new InvalidOperationException(
                $"Drawing job '{job.Id}' cannot be processed without an active lease.");
        }

        using var processingCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var heartbeatCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var leaseLost = 0;
        var heartbeatTask = MaintainLeaseAsync(
            job,
            processingCancellation,
            heartbeatCancellation.Token,
            () => Interlocked.Exchange(ref leaseLost, 1));

        try
        {
            var processingToken = processingCancellation.Token;
            await automationReadiness.WaitUntilReadyAsync(processingToken);
            var template = await templateCatalog.GetByIdOrCodeAsync(job.TemplateId, processingToken)
                ?? throw new InvalidOperationException($"Template '{job.TemplateId}' was not found.");

            var parameters = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                job.InputParametersJson,
                JsonOptions) ?? new Dictionary<string, object?>();

            var workingDirectory = await fileStorage.CreateWorkingDirectoryAsync(job, processingToken);
            EnsureLeaseOwned(await repository.UpdateWorkingDirectoryAsync(
                job.Id,
                workingDirectory,
                processingToken,
                job.LeaseToken));

            var templateCopyPath = await fileStorage.CopyTemplateToWorkingDirectoryAsync(
                template,
                workingDirectory,
                processingToken);

            var resultDirectory = fileStorage.CreateGeneratedDirectory(job);
            var generatedFiles = await automationClient.GenerateAsync(
                new TFlexGenerationRequest(
                    job,
                    template,
                    workingDirectory,
                    templateCopyPath,
                    resultDirectory,
                    parameters,
                    job.OutputFormat),
                processingToken);

            var namedFiles = RenameGeneratedFiles(job.Id, generatedFiles, parameters);
            foreach (var file in namedFiles)
            {
                EnsureLeaseOwned(await repository.AddGeneratedFileAsync(
                    file,
                    processingToken,
                    job.LeaseToken));
            }

            EnsureLeaseOwned(await repository.MarkCompletedAsync(
                job.Id,
                DateTimeOffset.UtcNow,
                processingToken,
                job.LeaseToken));
            logger.LogInformation("Drawing job {JobId} completed with {FileCount} file(s).", job.Id, namedFiles.Count);
        }
        catch (LeaseLostException)
        {
            logger.LogWarning(
                "Stopped drawing job {JobId} because its worker lease is no longer owned by this attempt.",
                job.Id);
        }
        catch (OperationCanceledException) when (Volatile.Read(ref leaseLost) == 1)
        {
            logger.LogWarning(
                "Cancelled drawing job {JobId} after its worker lease expired or was transferred.",
                job.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Drawing job {JobId} failed.", job.Id);
            var markedFailed = await repository.MarkFailedAsync(
                job.Id,
                exception.Message,
                DateTimeOffset.UtcNow,
                CancellationToken.None,
                job.LeaseToken);
            if (!markedFailed)
            {
                logger.LogWarning(
                    "Did not mark drawing job {JobId} failed because this attempt no longer owns the lease.",
                    job.Id);
            }
        }
        finally
        {
            await heartbeatCancellation.CancelAsync();
            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when processing completes or the host stops.
            }
        }
    }

    private async Task MaintainLeaseAsync(
        DrawingJob job,
        CancellationTokenSource processingCancellation,
        CancellationToken heartbeatCancellation,
        Action markLeaseLost)
    {
        var leaseExpiresAt = job.LeaseExpiresAt!.Value;
        while (!heartbeatCancellation.IsCancellationRequested)
        {
            var remaining = leaseExpiresAt - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                await CancelForLostLeaseAsync(processingCancellation, markLeaseLost);
                return;
            }

            var heartbeatDelay = queueOptions.Value.LeaseHeartbeatInterval;
            await Task.Delay(
                remaining < heartbeatDelay ? remaining : heartbeatDelay,
                heartbeatCancellation);

            remaining = leaseExpiresAt - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                await CancelForLostLeaseAsync(processingCancellation, markLeaseLost);
                return;
            }

            bool renewed;
            try
            {
                var renewedUntil = DateTimeOffset.UtcNow + queueOptions.Value.LeaseDuration;
                using var renewalCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(heartbeatCancellation);
                renewalCancellation.CancelAfter(remaining);
                var renewalTask = repository.RenewLeaseAsync(
                    job.Id,
                    job.LeaseToken!,
                    renewedUntil,
                    renewalCancellation.Token);
                using var expiryCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(heartbeatCancellation);
                var expiryTask = Task.Delay(remaining, expiryCancellation.Token);
                await Task.WhenAny(renewalTask, expiryTask);

                if (!renewalTask.IsCompleted)
                {
                    await renewalCancellation.CancelAsync();
                    _ = ObserveRenewalCompletionAsync(renewalTask);
                    heartbeatCancellation.ThrowIfCancellationRequested();
                    await CancelForLostLeaseAsync(processingCancellation, markLeaseLost);
                    return;
                }

                await expiryCancellation.CancelAsync();
                renewed = await renewalTask;
                if (renewed)
                {
                    leaseExpiresAt = renewedUntil;
                    job.LeaseExpiresAt = renewedUntil;
                }
            }
            catch (OperationCanceledException) when (heartbeatCancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Could not renew lease for drawing job {JobId}; the worker will retry before expiry.",
                    job.Id);

                if (DateTimeOffset.UtcNow >= leaseExpiresAt)
                {
                    await CancelForLostLeaseAsync(processingCancellation, markLeaseLost);
                    return;
                }

                continue;
            }

            if (renewed)
            {
                continue;
            }

            await CancelForLostLeaseAsync(processingCancellation, markLeaseLost);
            return;
        }
    }

    private static async Task CancelForLostLeaseAsync(
        CancellationTokenSource processingCancellation,
        Action markLeaseLost)
    {
        markLeaseLost();
        await processingCancellation.CancelAsync();
    }

    private static async Task ObserveRenewalCompletionAsync(Task<bool> renewalTask)
    {
        try
        {
            _ = await renewalTask;
        }
        catch
        {
            // The attempt is already fenced out. Observe any late failure so it
            // cannot surface as an unobserved task exception.
        }
    }

    private static void EnsureLeaseOwned(bool operationSucceeded)
    {
        if (!operationSucceeded)
        {
            throw new LeaseLostException();
        }
    }

    private static IReadOnlyList<GeneratedFile> RenameGeneratedFiles(
        string jobId,
        IReadOnlyList<GeneratedFile> files,
        IReadOnlyDictionary<string, object?> parameters)
    {
        var result = new List<GeneratedFile>(files.Count);
        var baseName = BuildResultFileBaseName(parameters);

        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            var extension = "." + file.Format.Trim().TrimStart('.').ToLowerInvariant();
            var suffix = files.Count == 1 ? string.Empty : $" - {index + 1}";
            var fileName = SanitizeFileName(baseName + suffix) + extension;
            var destinationPath = GetUniqueFilePath(Path.GetDirectoryName(file.Path)!, fileName, file.Path);

            if (!string.Equals(Path.GetFullPath(file.Path), destinationPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(file.Path, destinationPath);
            }

            var info = new FileInfo(destinationPath);
            file.JobId = jobId;
            file.FileName = info.Name;
            file.Path = info.FullName;
            file.SizeBytes = info.Length;
            result.Add(file);
        }

        return result;
    }

    private static string BuildResultFileBaseName(IReadOnlyDictionary<string, object?> parameters)
    {
        var liftNumber = GetParameterText(parameters, "$Oboznach", "№ лифта");
        var height = GetParameterText(parameters, "TR", "Высота");
        var address = GetParameterText(parameters, "$Address", "Адрес");

        return $"{liftNumber} ({height}) - {address}";
    }

    private static string GetParameterText(
        IReadOnlyDictionary<string, object?> parameters,
        string name,
        string fallback)
    {
        if (!parameters.TryGetValue(name, out var value))
        {
            return fallback;
        }

        var text = value switch
        {
            null => string.Empty,
            JsonElement { ValueKind: JsonValueKind.String } json => json.GetString() ?? string.Empty,
            JsonElement { ValueKind: JsonValueKind.Number } json => json.GetRawText(),
            JsonElement { ValueKind: JsonValueKind.True } => "Да",
            JsonElement { ValueKind: JsonValueKind.False } => "Нет",
            JsonElement json => json.ToString(),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        text = NormalizeFileNameText(text);
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }

    private static string NormalizeFileNameText(string value)
    {
        return string.Join(
            " ",
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value
            .Select(ch => invalid.Contains(ch) ? ' ' : ch)
            .ToArray();
        var sanitized = NormalizeFileNameText(new string(chars)).Trim(' ', '.');

        return string.IsNullOrWhiteSpace(sanitized)
            ? "result"
            : sanitized[..Math.Min(sanitized.Length, 180)];
    }

    private static string GetUniqueFilePath(string directory, string fileName, string currentPath)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path)
            || string.Equals(Path.GetFullPath(path), Path.GetFullPath(currentPath), StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(path);
        }

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 2; ; index++)
        {
            path = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(path))
            {
                return path;
            }
        }
    }

    private sealed class LeaseLostException : Exception
    {
    }
}
