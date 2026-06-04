using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Core.Abstractions;
using TFlexDrawingService.Core.Models;
using TFlexDrawingService.Infrastructure.Configuration;

namespace TFlexDrawingService.Infrastructure.Storage;

public sealed class JsonTemplateCatalog(
    IOptions<TemplateCatalogOptions> options,
    ILogger<JsonTemplateCatalog> logger) : ITemplateCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyList<DrawingTemplate>? _templates;
    private DateTimeOffset? _loadedWriteTimeUtc;

    public async Task<IReadOnlyList<DrawingTemplate>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(options.Value.ConfigPath))
        {
            throw new FileNotFoundException("Template configuration file was not found.", options.Value.ConfigPath);
        }

        var writeTimeUtc = File.GetLastWriteTimeUtc(options.Value.ConfigPath);
        if (_templates is not null && _loadedWriteTimeUtc == writeTimeUtc)
        {
            return _templates;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            writeTimeUtc = File.GetLastWriteTimeUtc(options.Value.ConfigPath);
            if (_templates is not null && _loadedWriteTimeUtc == writeTimeUtc)
            {
                return _templates;
            }

            await using var stream = File.OpenRead(options.Value.ConfigPath);
            var catalog = await JsonSerializer.DeserializeAsync<TemplateCatalogFile>(stream, JsonOptions, cancellationToken)
                ?? new TemplateCatalogFile();

            _templates = catalog.Templates
                .Select(NormalizeTemplate)
                .ToArray();
            _loadedWriteTimeUtc = writeTimeUtc;

            return _templates;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<DrawingTemplate?> GetByIdOrCodeAsync(
        string idOrCode,
        CancellationToken cancellationToken = default)
    {
        var templates = await ListAsync(cancellationToken);
        return templates.FirstOrDefault(template =>
            string.Equals(template.Id, idOrCode, StringComparison.OrdinalIgnoreCase)
            || string.Equals(template.Code, idOrCode, StringComparison.OrdinalIgnoreCase));
    }

    private DrawingTemplate NormalizeTemplate(DrawingTemplate template)
    {
        template.Code = template.Code.Trim();
        template.Id = string.IsNullOrWhiteSpace(template.Id) ? template.Code : template.Id.Trim();
        template.TemplateFilePath = ResolvePath(options.Value.ProjectRootPath, template.TemplateFilePath);
        template.OutputFormats = template.OutputFormats
            .Select(format => format.Trim().TrimStart('.').ToLowerInvariant())
            .Where(format => !string.IsNullOrWhiteSpace(format))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!File.Exists(template.TemplateFilePath))
        {
            logger.LogWarning(
                "Template file {TemplateFilePath} for template {TemplateCode} does not exist yet.",
                template.TemplateFilePath,
                template.Code);
        }

        return template;
    }

    private static string ResolvePath(string basePath, string path)
    {
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(basePath, path));
    }

    private sealed class TemplateCatalogFile
    {
        public List<DrawingTemplate> Templates { get; set; } = [];
    }
}
