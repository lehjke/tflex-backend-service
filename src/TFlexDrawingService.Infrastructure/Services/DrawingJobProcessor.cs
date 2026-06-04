using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TFlexDrawingService.Core.Abstractions;
using TFlexDrawingService.Core.Models;

namespace TFlexDrawingService.Infrastructure.Services;

public sealed class DrawingJobProcessor(
    ITemplateCatalog templateCatalog,
    IDrawingJobRepository repository,
    IFileStorage fileStorage,
    ITFlexAutomationClient automationClient,
    ILogger<DrawingJobProcessor> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task ProcessAsync(DrawingJob job, CancellationToken cancellationToken = default)
    {
        try
        {
            var template = await templateCatalog.GetByIdOrCodeAsync(job.TemplateId, cancellationToken)
                ?? throw new InvalidOperationException($"Template '{job.TemplateId}' was not found.");

            var parameters = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                job.InputParametersJson,
                JsonOptions) ?? new Dictionary<string, object?>();

            var workingDirectory = await fileStorage.CreateWorkingDirectoryAsync(job, cancellationToken);
            await repository.UpdateWorkingDirectoryAsync(job.Id, workingDirectory, cancellationToken);

            var templateCopyPath = await fileStorage.CopyTemplateToWorkingDirectoryAsync(
                template,
                workingDirectory,
                cancellationToken);

            var resultDirectory = fileStorage.CreateGeneratedDirectory(job.Id);
            var generatedFiles = await automationClient.GenerateAsync(
                new TFlexGenerationRequest(
                    job,
                    template,
                    workingDirectory,
                    templateCopyPath,
                    resultDirectory,
                    parameters,
                    job.OutputFormat),
                cancellationToken);

            var namedFiles = RenameGeneratedFiles(job.Id, generatedFiles, parameters);
            foreach (var file in namedFiles)
            {
                await repository.AddGeneratedFileAsync(file, cancellationToken);
            }

            await repository.MarkCompletedAsync(job.Id, DateTimeOffset.UtcNow, cancellationToken);
            logger.LogInformation("Drawing job {JobId} completed with {FileCount} file(s).", job.Id, namedFiles.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Drawing job {JobId} failed.", job.Id);
            await repository.MarkFailedAsync(job.Id, exception.Message, DateTimeOffset.UtcNow, CancellationToken.None);
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
}
