using System.Buffers;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Core.Models;
using TFlexDrawingService.Core.Services;
using TFlexDrawingService.Infrastructure.Configuration;

namespace TFlexDrawingService.Api.Data;

public sealed partial class TemplateImportService(IOptions<TemplateCatalogOptions> options)
{
    public const long MaxManifestBytes = 8 * 1024 * 1024;
    public const long MaxTemplateBytes = 512 * 1024 * 1024;
    public const long MaxFragmentsArchiveBytes = 512 * 1024 * 1024;
    public const long MaxFragmentsExpandedBytes = 1024L * 1024 * 1024;
    public const int MaxFragmentsEntries = 20_000;
    public const int MaxParameters = 2_000;
    public const int MaxCalculatedVariables = 4_000;
    public const int MaxValidationRules = 2_000;
    public const int MaxLookupTables = 64;
    public const int MaxLookupRowsPerTable = 5_000;
    public const int MaxInlineLookupRowsPerDefinition = 5_000;
    public const int MaxLookupFieldsPerRow = 128;
    public const int MaxLookupRowsTotal = 20_000;
    public const int MaxLookupFieldsTotal = 200_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        MaxDepth = 256
    };

    private static readonly HashSet<string> AllowedOutputFormats =
        new(StringComparer.OrdinalIgnoreCase) { "pdf", "dwg", "dxf" };

    private static readonly HashSet<string> BlockedFragmentExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".bat", ".cmd", ".com", ".dll", ".exe", ".hta", ".js", ".jse",
            ".lnk", ".msi", ".msp", ".ps1", ".psd1", ".psm1", ".scr", ".vbe",
            ".vbs", ".wsf", ".wsh"
        };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TemplateCatalogOptions _options = options.Value;

    public async Task<TemplateImportResult> ImportAsync(
        IFormFile? manifestFile,
        IFormFile? templateFile,
        IFormFile? fragmentsArchive,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateFiles(manifestFile, templateFile, fragmentsArchive);
        if (errors.Count > 0)
        {
            return TemplateImportResult.Failure(errors);
        }

        DrawingTemplate template;
        try
        {
            template = await ReadManifestAsync(manifestFile!, cancellationToken);
            NormalizeTemplateCollections(template);
        }
        catch (JsonException exception)
        {
            return TemplateImportResult.Failure(
                "manifest",
                $"Manifest JSON is invalid: {exception.Message}");
        }

        ValidateTemplateDefinition(template, errors);
        foreach (var expressionError in TemplateExpressionDefinitionValidator.Validate(template))
        {
            errors[expressionError.Field] = [expressionError.Message];
        }

        if (errors.Count > 0)
        {
            return TemplateImportResult.Failure(errors);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ImportUnderLockAsync(
                template,
                templateFile!,
                fragmentsArchive,
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<TemplateImportResult> ImportUnderLockAsync(
        DrawingTemplate template,
        IFormFile templateFile,
        IFormFile? fragmentsArchive,
        CancellationToken cancellationToken)
    {
        var catalog = await ReadCatalogAsync(cancellationToken);
        if (catalog.Templates.Any(existing =>
                string.Equals(existing.Id, template.Id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(existing.Code, template.Code, StringComparison.OrdinalIgnoreCase)))
        {
            return TemplateImportResult.Failure(
                "manifest",
                $"Template id '{template.Id}' or code '{template.Code}' already exists.");
        }

        var catalogDirectory = Path.GetDirectoryName(_options.ConfigPath)
            ?? throw new InvalidOperationException("Template catalog path has no parent directory.");
        Directory.CreateDirectory(catalogDirectory);

        var importedRoot = Path.GetFullPath(Path.Combine(catalogDirectory, "imported"));
        Directory.CreateDirectory(importedRoot);
        var destinationDirectory = Path.GetFullPath(Path.Combine(importedRoot, template.Id));
        if (!IsPathUnderRoot(destinationDirectory, importedRoot) || File.Exists(destinationDirectory))
        {
            return TemplateImportResult.Failure(
                "manifest",
                $"Template storage directory for '{template.Id}' already exists.");
        }

        if (Directory.Exists(destinationDirectory))
        {
            try
            {
                var recoveryRoot = Path.Combine(importedRoot, ".recovered-orphans");
                Directory.CreateDirectory(recoveryRoot);
                var recoveryDirectory = Path.Combine(
                    recoveryRoot,
                    $"{template.Id}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():n}");
                Directory.Move(destinationDirectory, recoveryDirectory);
            }
            catch (IOException exception)
            {
                return TemplateImportResult.Failure(
                    "files",
                    $"An incomplete earlier import could not be recovered: {exception.Message}");
            }
            catch (UnauthorizedAccessException exception)
            {
                return TemplateImportResult.Failure(
                    "files",
                    $"An incomplete earlier import could not be recovered: {exception.Message}");
            }
        }

        var stagingDirectory = Path.Combine(
            catalogDirectory,
            $".template-import-{Guid.NewGuid():n}");
        var catalogTempPath = Path.Combine(
            catalogDirectory,
            $".templates-{Guid.NewGuid():n}.tmp");

        Directory.CreateDirectory(stagingDirectory);
        var destinationCommitted = false;
        try
        {
            var templateFileName = SafeLeafFileName(templateFile.FileName);
            var stagedTemplatePath = Path.Combine(stagingDirectory, templateFileName);
            await SaveFormFileAsync(templateFile, stagedTemplatePath, cancellationToken);

            if (fragmentsArchive is not null)
            {
                var fragmentsDirectory = Path.Combine(
                    stagingDirectory,
                    Path.GetFileNameWithoutExtension(templateFileName));
                Directory.CreateDirectory(fragmentsDirectory);
                await ExtractFragmentsAsync(
                    fragmentsArchive,
                    fragmentsDirectory,
                    cancellationToken);
            }

            template.TemplateFilePath = ToCatalogPath(
                Path.Combine(destinationDirectory, templateFileName));
            template.OutputFormats = template.OutputFormats
                .Select(NormalizeFormat)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            catalog.Templates.Add(template);

            await WriteCatalogAsync(catalogTempPath, catalog, cancellationToken);
            Directory.Move(stagingDirectory, destinationDirectory);
            destinationCommitted = true;
            File.Move(catalogTempPath, _options.ConfigPath, overwrite: true);

            return TemplateImportResult.Success(template);
        }
        catch (InvalidDataException exception)
        {
            return TemplateImportResult.Failure("fragments", exception.Message);
        }
        catch (IOException exception)
        {
            return TemplateImportResult.Failure(
                "files",
                $"Template files could not be stored: {exception.Message}");
        }
        finally
        {
            TryDeleteFile(catalogTempPath);
            TryDeleteDirectory(stagingDirectory);
            if (destinationCommitted && !CatalogContainsTemplatePath(template.TemplateFilePath))
            {
                TryDeleteDirectory(destinationDirectory);
            }
        }
    }

    private static Dictionary<string, string[]> ValidateFiles(
        IFormFile? manifestFile,
        IFormFile? templateFile,
        IFormFile? fragmentsArchive)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        ValidateRequiredFile(manifestFile, "manifest", MaxManifestBytes, [".json"], errors);
        ValidateRequiredFile(templateFile, "template", MaxTemplateBytes, [".grb"], errors);

        if (fragmentsArchive is not null)
        {
            ValidateRequiredFile(
                fragmentsArchive,
                "fragments",
                MaxFragmentsArchiveBytes,
                [".zip"],
                errors);
        }

        return errors;
    }

    private static void ValidateRequiredFile(
        IFormFile? file,
        string fieldName,
        long maxBytes,
        IReadOnlyCollection<string> allowedExtensions,
        IDictionary<string, string[]> errors)
    {
        if (file is null || file.Length <= 0)
        {
            errors[fieldName] = [$"{fieldName} file is required."];
            return;
        }

        if (file.Length > maxBytes)
        {
            errors[fieldName] = [$"{fieldName} file exceeds the {maxBytes / 1024 / 1024} MB limit."];
            return;
        }

        string extension;
        try
        {
            extension = Path.GetExtension(SafeLeafFileName(file.FileName));
        }
        catch (InvalidDataException exception)
        {
            errors[fieldName] = [exception.Message];
            return;
        }

        if (!allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            errors[fieldName] =
                [$"{fieldName} file must use: {string.Join(", ", allowedExtensions)}."];
        }
    }

    private static async Task<DrawingTemplate> ReadManifestAsync(
        IFormFile manifestFile,
        CancellationToken cancellationToken)
    {
        await using var stream = manifestFile.OpenReadStream();
        using var document = await JsonDocument.ParseAsync(
            stream,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = JsonOptions.MaxDepth
            },
            cancellationToken);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Manifest root must be a JSON object.");
        }

        if (document.RootElement.TryGetProperty("templates", out var templatesElement))
        {
            if (templatesElement.ValueKind != JsonValueKind.Array
                || templatesElement.GetArrayLength() != 1)
            {
                throw new JsonException("Catalog-style manifest must contain exactly one template.");
            }

            return templatesElement[0].Deserialize<DrawingTemplate>(JsonOptions)
                ?? throw new JsonException("Template definition is empty.");
        }

        return document.RootElement.Deserialize<DrawingTemplate>(JsonOptions)
            ?? throw new JsonException("Template definition is empty.");
    }

    private static void ValidateTemplateDefinition(
        DrawingTemplate template,
        IDictionary<string, string[]> errors)
    {
        template.Id = template.Id?.Trim() ?? string.Empty;
        template.Code = template.Code?.Trim() ?? string.Empty;
        template.Name = template.Name?.Trim() ?? string.Empty;

        if (!TemplateIdRegex().IsMatch(template.Id))
        {
            errors["manifest.id"] =
                ["Id must be 1-80 ASCII letters, digits, underscores or hyphens and start with a letter or digit."];
        }

        if (!TemplateCodeRegex().IsMatch(template.Code))
        {
            errors["manifest.code"] =
                ["Code must be 1-80 ASCII letters, digits, dots, underscores or hyphens and start with a letter or digit."];
        }

        if (string.IsNullOrWhiteSpace(template.Name) || template.Name.Length > 200)
        {
            errors["manifest.name"] = ["Name is required and must not exceed 200 characters."];
        }

        var formats = template.OutputFormats
            .Select(NormalizeFormat)
            .Where(format => !string.IsNullOrWhiteSpace(format))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (formats.Length == 0 || formats.Any(format => !AllowedOutputFormats.Contains(format)))
        {
            errors["manifest.outputFormats"] = ["At least one of pdf, dwg or dxf is required."];
        }

        if (template.Parameters.Count > MaxParameters)
        {
            errors["manifest.parameters"] =
                [$"A template may contain at most {MaxParameters} parameters."];
        }

        if (template.CalculatedVariables.Count > MaxCalculatedVariables)
        {
            errors["manifest.calculatedVariables"] =
                [$"A template may contain at most {MaxCalculatedVariables} calculated variables."];
        }

        if (template.ValidationRules.Count > MaxValidationRules)
        {
            errors["manifest.validationRules"] =
                [$"A template may contain at most {MaxValidationRules} validation rules."];
        }

        ValidateUniqueNames(template.Parameters, "manifest.parameters", errors);
        ValidateUniqueNames(template.CalculatedVariables, "manifest.calculatedVariables", errors);
        ValidateDefinitions(template.Parameters, "manifest.parameters", requireExpression: false, errors);
        ValidateDefinitions(
            template.CalculatedVariables,
            "manifest.calculatedVariables",
            requireExpression: true,
            errors);

        var duplicateContextNames = template.Parameters
            .Where(definition => definition is not null && !string.IsNullOrWhiteSpace(definition.Name))
            .Select(definition => definition.Name.Trim())
            .Intersect(
                template.CalculatedVariables
                    .Where(definition => definition is not null && !string.IsNullOrWhiteSpace(definition.Name))
                    .Select(definition => definition.Name.Trim()),
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (duplicateContextNames.Length > 0)
        {
            errors["manifest.calculatedVariables"] =
                ["Calculated-variable names must not duplicate input-parameter names."];
        }

        ValidateLookupBudgets(template, errors);

        for (var index = 0; index < template.ValidationRules.Count; index++)
        {
            var rule = template.ValidationRules[index];
            if (rule is null
                || string.IsNullOrWhiteSpace(rule.Name)
                || string.IsNullOrWhiteSpace(rule.Expression))
            {
                errors[$"manifest.validationRules[{index}]"] =
                    ["Validation rule name and expression are required."];
            }
        }
    }

    private static void ValidateLookupBudgets(
        DrawingTemplate template,
        IDictionary<string, string[]> errors)
    {
        if (template.LookupTables.Count > MaxLookupTables)
        {
            errors["manifest.lookupTables"] =
                [$"A template may contain at most {MaxLookupTables} lookup tables."];
        }

        long totalRows = 0;
        long totalFields = 0;
        var tableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (tableName, rows) in template.LookupTables)
        {
            if (string.IsNullOrWhiteSpace(tableName)
                || tableName.Length > 128
                || !tableNames.Add(tableName))
            {
                errors["manifest.lookupTables"] =
                    ["Lookup-table names must be unique, non-empty, and no longer than 128 characters."];
                continue;
            }

            if (rows is null || rows.Count == 0)
            {
                errors[$"manifest.lookupTables.{tableName}"] =
                    ["A lookup table must contain at least one row."];
                continue;
            }

            if (rows.Count > MaxLookupRowsPerTable)
            {
                errors[$"manifest.lookupTables.{tableName}"] =
                    [$"A lookup table may contain at most {MaxLookupRowsPerTable} rows."];
            }

            totalRows += rows.Count;
            ValidateLookupRows(
                rows,
                $"manifest.lookupTables.{tableName}",
                ref totalFields,
                errors);
        }

        foreach (var (definition, index, fieldName) in template.Parameters
                     .Select((definition, index) => (definition, index, "manifest.parameters"))
                     .Concat(template.CalculatedVariables.Select(
                         (definition, index) => (definition, index, "manifest.calculatedVariables"))))
        {
            if (definition is null || definition.LookupValues.Count == 0)
            {
                continue;
            }

            if (definition.LookupValues.Count > MaxInlineLookupRowsPerDefinition)
            {
                errors[$"{fieldName}[{index}].lookupValues"] =
                    [$"Inline lookup values may contain at most {MaxInlineLookupRowsPerDefinition} rows."];
            }

            totalRows += definition.LookupValues.Count;
            ValidateLookupRows(
                definition.LookupValues,
                $"{fieldName}[{index}].lookupValues",
                ref totalFields,
                errors);
        }

        if (totalRows > MaxLookupRowsTotal)
        {
            errors["manifest.lookupTables"] =
                [$"A template may contain at most {MaxLookupRowsTotal} lookup rows in total."];
        }

        if (totalFields > MaxLookupFieldsTotal)
        {
            errors["manifest.lookupTables"] =
                [$"A template may contain at most {MaxLookupFieldsTotal} lookup fields in total."];
        }
    }

    private static void ValidateLookupRows(
        IReadOnlyList<Dictionary<string, JsonElement>> rows,
        string fieldName,
        ref long totalFields,
        IDictionary<string, string[]> errors)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row is null || row.Count == 0)
            {
                errors[$"{fieldName}[{index}]"] =
                    ["A lookup row must be a non-empty JSON object."];
                continue;
            }

            totalFields += row.Count;
            if (row.Count > MaxLookupFieldsPerRow)
            {
                errors[$"{fieldName}[{index}]"] =
                    [$"A lookup row may contain at most {MaxLookupFieldsPerRow} fields."];
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (row.Keys.Any(name =>
                    string.IsNullOrWhiteSpace(name)
                    || name.Length > 128
                    || !names.Add(name)))
            {
                errors[$"{fieldName}[{index}]"] =
                    ["Lookup field names must be unique, non-empty, and no longer than 128 characters."];
            }
        }
    }

    private static void ValidateDefinitions(
        IReadOnlyList<DrawingParameterDefinition> definitions,
        string fieldName,
        bool requireExpression,
        IDictionary<string, string[]> errors)
    {
        var supportedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "number", "integer", "bool", "boolean", "string", "enum"
        };

        for (var index = 0; index < definitions.Count; index++)
        {
            var definition = definitions[index];
            if (definition is null)
            {
                errors[$"{fieldName}[{index}]"] = ["Parameter definition is required."];
                continue;
            }

            var type = string.IsNullOrWhiteSpace(definition.Type)
                ? "string"
                : definition.Type.Trim();
            if (!supportedTypes.Contains(type))
            {
                errors[$"{fieldName}[{index}].type"] =
                    ["Type must be number, integer, bool, boolean, string or enum."];
            }
            else if ((type.Equals("string", StringComparison.OrdinalIgnoreCase)
                      || type.Equals("enum", StringComparison.OrdinalIgnoreCase))
                     && !string.IsNullOrWhiteSpace(definition.Name)
                     && !definition.Name.Trim().StartsWith('$'))
            {
                errors[$"{fieldName}[{index}].type"] =
                    ["T-FLEX text variables must use a name beginning with '$'; real variables accept only numeric or boolean types."];
            }

            if (definition.MinValue.HasValue
                && definition.MaxValue.HasValue
                && definition.MinValue > definition.MaxValue)
            {
                errors[$"{fieldName}[{index}]"] =
                    ["Minimum value must not exceed maximum value."];
            }

            if (requireExpression && string.IsNullOrWhiteSpace(definition.Expression))
            {
                errors[$"{fieldName}[{index}].expression"] =
                    ["Calculated variables require an expression."];
            }
        }
    }

    private static void NormalizeTemplateCollections(DrawingTemplate template)
    {
        template.OutputFormats ??= [];
        template.Parameters ??= [];
        template.CalculatedVariables ??= [];
        template.ValidationRules ??= [];
        template.LookupTables ??= [];

        foreach (var definition in template.Parameters.Concat(template.CalculatedVariables))
        {
            if (definition is null)
            {
                continue;
            }

            definition.LookupValues ??= [];
            definition.AllowedValues ??= [];
            definition.AllowedValueLabels ??= [];
        }

        foreach (var rule in template.ValidationRules)
        {
            if (rule is not null)
            {
                rule.FieldNames ??= [];
            }
        }
    }

    private static void ValidateUniqueNames(
        IReadOnlyCollection<DrawingParameterDefinition> definitions,
        string fieldName,
        IDictionary<string, string[]> errors)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasInvalidName = definitions.Any(definition =>
            definition is null
            || string.IsNullOrWhiteSpace(definition.Name)
            || !names.Add(definition.Name.Trim()));
        if (hasInvalidName)
        {
            errors[fieldName] = ["Parameter names must be non-empty and unique within their section."];
        }
    }

    private async Task<TemplateCatalogFile> ReadCatalogAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_options.ConfigPath))
        {
            throw new FileNotFoundException(
                "Template configuration file was not found.",
                _options.ConfigPath);
        }

        await using var stream = File.OpenRead(_options.ConfigPath);
        var catalog = await JsonSerializer.DeserializeAsync<TemplateCatalogFile>(
                stream,
                JsonOptions,
                cancellationToken)
            ?? new TemplateCatalogFile();
        catalog.Templates ??= [];
        return catalog;
    }

    private static async Task WriteCatalogAsync(
        string path,
        TemplateCatalogFile catalog,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, catalog, JsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task SaveFormFileAsync(
        IFormFile file,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = file.OpenReadStream();
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await source.CopyToAsync(destination, cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }

    private static async Task ExtractFragmentsAsync(
        IFormFile archiveFile,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        await using var archiveStream = archiveFile.OpenReadStream();
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false);

        if (archive.Entries.Count > MaxFragmentsEntries)
        {
            throw new InvalidDataException(
                $"Fragments archive contains more than {MaxFragmentsEntries} entries.");
        }

        long expandedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsSymbolicLink(entry))
            {
                throw new InvalidDataException(
                    $"Fragments archive contains a symbolic link: '{entry.FullName}'.");
            }

            var remainingBytes = MaxFragmentsExpandedBytes - expandedBytes;
            if (entry.Length > remainingBytes)
            {
                throw new InvalidDataException(
                    $"Expanded fragments exceed the {MaxFragmentsExpandedBytes / 1024 / 1024} MB limit.");
            }

            var relativePath = NormalizeArchivePath(entry.FullName);
            if (relativePath.Length == 0)
            {
                continue;
            }

            var destinationPath = Path.GetFullPath(Path.Combine(destinationRoot, relativePath));
            if (!IsPathUnderRoot(destinationPath, destinationRoot))
            {
                throw new InvalidDataException(
                    $"Fragments archive contains an unsafe path: '{entry.FullName}'.");
            }

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal)
                || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            if (BlockedFragmentExtensions.Contains(Path.GetExtension(destinationPath)))
            {
                throw new InvalidDataException(
                    $"Fragments archive contains a blocked file type: '{entry.FullName}'.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using var source = entry.Open();
            await using var destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            var copiedBytes = await CopyWithByteLimitAsync(
                source,
                destination,
                remainingBytes,
                cancellationToken);
            if (copiedBytes != entry.Length)
            {
                throw new InvalidDataException(
                    $"Fragments archive entry '{entry.FullName}' has inconsistent size metadata.");
            }

            expandedBytes += copiedBytes;
            await destination.FlushAsync(cancellationToken);
        }
    }

    internal static async Task<long> CopyWithByteLimitAsync(
        Stream source,
        Stream destination,
        long maxBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);

        const int BufferSize = 128 * 1024;
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            long copiedBytes = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remainingBytes = maxBytes - copiedBytes;
                var readSize = (int)Math.Min(
                    buffer.Length,
                    remainingBytes < buffer.Length ? remainingBytes + 1 : buffer.Length);
                var bytesRead = await source.ReadAsync(
                    buffer.AsMemory(0, readSize),
                    cancellationToken);
                if (bytesRead == 0)
                {
                    return copiedBytes;
                }

                if (bytesRead > remainingBytes)
                {
                    throw new InvalidDataException(
                        $"Expanded fragments exceed the {MaxFragmentsExpandedBytes / 1024 / 1024} MB limit.");
                }

                await destination.WriteAsync(
                    buffer.AsMemory(0, bytesRead),
                    cancellationToken);
                copiedBytes += bytesRead;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string NormalizeArchivePath(string path)
    {
        var portablePath = path.Replace('\\', '/');
        if (portablePath.StartsWith("/", StringComparison.Ordinal)
            || portablePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()?.Contains(':') == true)
        {
            throw new InvalidDataException($"Fragments archive contains an unsafe path: '{path}'.");
        }

        var normalized = portablePath;
        if (normalized.Contains('\0')
            || normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException($"Fragments archive contains an unsafe path: '{path}'.");
        }

        return normalized.Replace('/', Path.DirectorySeparatorChar);
    }

    private string ToCatalogPath(string absolutePath)
    {
        var projectRoot = Path.GetFullPath(_options.ProjectRootPath);
        if (!IsPathUnderRoot(absolutePath, projectRoot))
        {
            throw new IOException("Imported template path is outside the configured project root.");
        }

        return Path.GetRelativePath(projectRoot, absolutePath)
            .Replace(Path.DirectorySeparatorChar, '/');
    }

    private bool CatalogContainsTemplatePath(string relativeTemplatePath)
    {
        try
        {
            if (!File.Exists(_options.ConfigPath))
            {
                return false;
            }

            var json = File.ReadAllText(_options.ConfigPath);
            var catalog = JsonSerializer.Deserialize<TemplateCatalogFile>(json, JsonOptions);
            return catalog?.Templates.Any(template =>
                string.Equals(
                    template.TemplateFilePath.Replace('\\', '/'),
                    relativeTemplatePath.Replace('\\', '/'),
                    StringComparison.OrdinalIgnoreCase)) == true;
        }
        catch
        {
            return false;
        }
    }

    private static string SafeLeafFileName(string fileName)
    {
        var leaf = Path.GetFileName(fileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(leaf)
            || leaf is "." or ".."
            || leaf.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException("Uploaded file name is invalid.");
        }

        return leaf;
    }

    private static string NormalizeFormat(string format)
    {
        return (format ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
    }

    private static bool IsPathUnderRoot(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var normalizedRoot = Path.GetFullPath(root);
        if (!normalizedRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            normalizedRoot += Path.DirectorySeparatorChar;
        }

        return Path.GetFullPath(path).StartsWith(normalizedRoot, comparison);
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        const int unixFileTypeMask = 0xF000;
        const int unixSymbolicLink = 0xA000;
        return ((entry.ExternalAttributes >> 16) & unixFileTypeMask) == unixSymbolicLink;
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
            // A failed cleanup must not hide the import result.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // A failed cleanup must not hide the import result.
        }
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,79}$", RegexOptions.CultureInvariant)]
    private static partial Regex TemplateIdRegex();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$", RegexOptions.CultureInvariant)]
    private static partial Regex TemplateCodeRegex();

    private sealed class TemplateCatalogFile
    {
        public List<DrawingTemplate> Templates { get; set; } = [];
    }
}

public sealed record TemplateImportResult(
    bool IsSuccess,
    DrawingTemplate? Template,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public static TemplateImportResult Success(DrawingTemplate template)
    {
        return new TemplateImportResult(
            true,
            template,
            new Dictionary<string, string[]>());
    }

    public static TemplateImportResult Failure(
        IReadOnlyDictionary<string, string[]> errors)
    {
        return new TemplateImportResult(false, null, errors);
    }

    public static TemplateImportResult Failure(string field, string error)
    {
        return Failure(new Dictionary<string, string[]> { [field] = [error] });
    }
}
