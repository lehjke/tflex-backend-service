using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using TFlexDrawingService.Core.Models;
using TFlexDrawingService.Infrastructure.Configuration;
using TFlexDrawingService.Infrastructure.Persistence;

namespace TFlexDrawingService.Api.Data;

public sealed class TemplateAnalysisUploadService(
    TemplateAnalysisStore store,
    IOptions<DrawingStorageOptions> storageOptions,
    IOptions<DrawingQueueOptions> queueOptions)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        MaxDepth = 256
    };

    private readonly DrawingStorageOptions _storageOptions = storageOptions.Value;

    public async Task<TemplateAnalysisCreateResult> CreateAsync(
        IFormFile? templateFile,
        IFormFile? componentsArchive,
        string ownerUserName,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateUpload(templateFile, componentsArchive);
        if (errors.Count > 0)
        {
            return TemplateAnalysisCreateResult.Failure(errors);
        }

        var job = new TemplateAnalysisJob
        {
            OwnerUserName = ownerUserName,
            OriginalTemplateFileName = TemplateImportService.SafeLeafFileName(templateFile!.FileName)
        };
        var analysisRoot = Path.GetFullPath(Path.Combine(_storageOptions.RootPath, "template-analysis"));
        var jobDirectory = Path.GetFullPath(Path.Combine(analysisRoot, job.Id));
        if (!IsPathUnderRoot(jobDirectory, analysisRoot))
        {
            return TemplateAnalysisCreateResult.Failure("files", "The analysis storage path is invalid.");
        }

        Directory.CreateDirectory(jobDirectory);
        try
        {
            job.TemplateFilePath = Path.Combine(jobDirectory, job.OriginalTemplateFileName);
            await TemplateImportService.SaveFormFileAsync(
                templateFile,
                job.TemplateFilePath,
                cancellationToken);

            if (componentsArchive is not null)
            {
                var archivePath = Path.Combine(jobDirectory, "components.zip");
                await TemplateImportService.SaveFormFileAsync(
                    componentsArchive,
                    archivePath,
                    cancellationToken);
                job.ComponentsDirectoryPath = Path.Combine(
                    jobDirectory,
                    Path.GetFileNameWithoutExtension(job.OriginalTemplateFileName));
                Directory.CreateDirectory(job.ComponentsDirectoryPath);
                await using var archiveStream = File.OpenRead(archivePath);
                var storedArchive = new FormFile(
                    archiveStream,
                    0,
                    archiveStream.Length,
                    "fragments",
                    "components.zip");
                await TemplateImportService.ExtractFragmentsAsync(
                    storedArchive,
                    job.ComponentsDirectoryPath,
                    cancellationToken);
            }

            if (!await store.TryCreateAsync(
                    job,
                    queueOptions.Value.MaxActiveJobs,
                    cancellationToken))
            {
                TemplateImportService.TryDeleteDirectory(jobDirectory);
                return TemplateAnalysisCreateResult.Failure(
                    "queue",
                    "The template analysis queue is full. Try again after an active analysis finishes.");
            }

            return TemplateAnalysisCreateResult.Success(job);
        }
        catch (InvalidDataException exception)
        {
            TemplateImportService.TryDeleteDirectory(jobDirectory);
            return TemplateAnalysisCreateResult.Failure("components", exception.Message);
        }
        catch
        {
            TemplateImportService.TryDeleteDirectory(jobDirectory);
            throw;
        }
    }

    public async Task<TemplateImportResult> PublishAsync(
        TemplateAnalysisJob job,
        TemplateImportService importer,
        CancellationToken cancellationToken = default)
    {
        if (job.Status != TemplateAnalysisStatus.Completed)
        {
            return TemplateImportResult.Failure(
                "analysis",
                "Only a completed template analysis can be published.");
        }

        DrawingTemplate? draft;
        try
        {
            draft = JsonSerializer.Deserialize<DrawingTemplate>(job.DraftJson, JsonOptions);
        }
        catch (JsonException exception)
        {
            return TemplateImportResult.Failure("draft", $"Draft JSON is invalid: {exception.Message}");
        }

        if (draft is null)
        {
            return TemplateImportResult.Failure("draft", "Template draft is empty.");
        }

        var manifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(draft, JsonOptions));
        await using var manifestStream = new MemoryStream(manifestBytes, writable: false);
        await using var templateStream = File.OpenRead(job.TemplateFilePath);
        var manifest = CreateFormFile(manifestStream, "manifest", "template.json", "application/json");
        var template = CreateFormFile(
            templateStream,
            "template",
            job.OriginalTemplateFileName,
            "application/octet-stream");

        var archivePath = Path.Combine(Path.GetDirectoryName(job.TemplateFilePath)!, "components.zip");
        if (!File.Exists(archivePath))
        {
            return await importer.ImportAsync(manifest, template, null, cancellationToken);
        }

        await using var archiveStream = File.OpenRead(archivePath);
        var archive = CreateFormFile(archiveStream, "fragments", "components.zip", "application/zip");
        return await importer.ImportAsync(manifest, template, archive, cancellationToken);
    }

    public static bool TryNormalizeDraft(
        JsonElement payload,
        out DrawingTemplate? template,
        out string? error)
    {
        template = null;
        error = null;
        try
        {
            template = payload.Deserialize<DrawingTemplate>(JsonOptions);
            if (template is null)
            {
                error = "Template draft is empty.";
                return false;
            }

            var serialized = JsonSerializer.Serialize(template, JsonOptions);
            if (Encoding.UTF8.GetByteCount(serialized) > TemplateImportService.MaxManifestBytes)
            {
                error = "Template draft exceeds the manifest size limit.";
                template = null;
                return false;
            }

            return true;
        }
        catch (JsonException exception)
        {
            error = $"Template draft is invalid: {exception.Message}";
            return false;
        }
    }

    public static string SerializeDraft(DrawingTemplate template)
    {
        return JsonSerializer.Serialize(template, JsonOptions);
    }

    private static Dictionary<string, string[]> ValidateUpload(
        IFormFile? templateFile,
        IFormFile? componentsArchive)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        ValidateFile(
            templateFile,
            "template",
            TemplateImportService.MaxTemplateBytes,
            ".grb",
            required: true,
            errors);
        ValidateFile(
            componentsArchive,
            "components",
            TemplateImportService.MaxFragmentsArchiveBytes,
            ".zip",
            required: false,
            errors);
        return errors;
    }

    private static void ValidateFile(
        IFormFile? file,
        string field,
        long maxBytes,
        string extension,
        bool required,
        IDictionary<string, string[]> errors)
    {
        if (file is null)
        {
            if (required)
            {
                errors[field] = [$"{field} file is required."];
            }

            return;
        }

        if (file.Length <= 0 || file.Length > maxBytes)
        {
            errors[field] = [$"{field} file must be between 1 byte and {maxBytes / 1024 / 1024} MB."];
            return;
        }

        try
        {
            if (!Path.GetExtension(TemplateImportService.SafeLeafFileName(file.FileName))
                    .Equals(extension, StringComparison.OrdinalIgnoreCase))
            {
                errors[field] = [$"{field} file must use {extension}."];
            }
        }
        catch (InvalidDataException exception)
        {
            errors[field] = [exception.Message];
        }
    }

    private static FormFile CreateFormFile(
        Stream stream,
        string name,
        string fileName,
        string contentType)
    {
        return new FormFile(stream, 0, stream.Length, name, fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
            ContentDisposition = new ContentDispositionHeaderValue("form-data").ToString()
        };
    }

    private static bool IsPathUnderRoot(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(normalizedRoot, comparison);
    }
}

public sealed record TemplateAnalysisCreateResult(
    bool IsSuccess,
    TemplateAnalysisJob? Job,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public static TemplateAnalysisCreateResult Success(TemplateAnalysisJob job)
    {
        return new TemplateAnalysisCreateResult(true, job, new Dictionary<string, string[]>());
    }

    public static TemplateAnalysisCreateResult Failure(
        IReadOnlyDictionary<string, string[]> errors)
    {
        return new TemplateAnalysisCreateResult(false, null, errors);
    }

    public static TemplateAnalysisCreateResult Failure(string field, string error)
    {
        return Failure(new Dictionary<string, string[]> { [field] = [error] });
    }
}
