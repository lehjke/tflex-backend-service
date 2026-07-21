using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Api.Data;
using TFlexDrawingService.Core.Requests;
using TFlexDrawingService.Core.Services;
using TFlexDrawingService.Infrastructure.Configuration;
using TFlexDrawingService.Tests.Support;

namespace TFlexDrawingService.Tests;

public sealed class TemplateImportServiceTests
{
    [Fact]
    public async Task CopyWithByteLimitAsync_StopsBeforeWritingPastBudget()
    {
        await using var source = new MemoryStream(new byte[17]);
        await using var destination = new MemoryStream();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => TemplateImportService.CopyWithByteLimitAsync(
                source,
                destination,
                maxBytes: 16));

        Assert.Contains("exceed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(destination.Length, 0, 16);
    }

    [Fact]
    public async Task ImportAsync_AddsTemplateAndFragmentsAtomically()
    {
        var root = CreateRoot();
        var service = CreateService(root);
        var manifest = CreateFormFile(
            "manifest.json",
            """
            {
              "id": "new_template",
              "code": "NEW-TEMPLATE",
              "name": "New template",
              "outputFormats": ["PDF", "dwg"],
              "parameters": [
                { "name": "AH", "type": "number", "isRequired": true }
              ],
              "calculatedVariables": [],
              "validationRules": [],
              "lookupTables": {}
            }
            """);
        var template = CreateFormFile("New template.grb", "t-flex");
        var fragments = CreateZipFile(
            "fragments.zip",
            ("nested/fragment.grb", "fragment"),
            ("table.xlsx", "table"));

        var result = await service.ImportAsync(manifest, template, fragments);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Template);
        Assert.Equal(["pdf", "dwg"], result.Template.OutputFormats);
        Assert.Equal(
            "templates/imported/new_template/New template.grb",
            result.Template.TemplateFilePath);
        Assert.True(File.Exists(Path.Combine(
            root,
            "templates",
            "imported",
            "new_template",
            "New template.grb")));
        Assert.True(File.Exists(Path.Combine(
            root,
            "templates",
            "imported",
            "new_template",
            "New template",
            "nested",
            "fragment.grb")));

        using var catalog = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(root, "templates", "templates.json")));
        var imported = Assert.Single(catalog.RootElement.GetProperty("templates").EnumerateArray());
        Assert.Equal("new_template", imported.GetProperty("id").GetString());
        Assert.Equal(
            "templates/imported/new_template/New template.grb",
            imported.GetProperty("templateFilePath").GetString());
    }

    [Fact]
    public async Task ImportAsync_RejectsUnsafeArchivePathWithoutChangingCatalog()
    {
        var root = CreateRoot();
        var service = CreateService(root);
        var manifest = CreateFormFile(
            "manifest.json",
            """
            {
              "id": "unsafe_template",
              "code": "UNSAFE-TEMPLATE",
              "name": "Unsafe template",
              "outputFormats": ["pdf"]
            }
            """);
        var fragments = CreateZipFile("fragments.zip", ("../escape.grb", "escape"));

        var result = await service.ImportAsync(
            manifest,
            CreateFormFile("Unsafe.grb", "t-flex"),
            fragments);

        Assert.False(result.IsSuccess);
        Assert.Contains("unsafe path", result.Errors["fragments"][0], StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(
            root,
            "templates",
            "imported",
            "unsafe_template")));

        using var catalog = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(root, "templates", "templates.json")));
        Assert.Empty(catalog.RootElement.GetProperty("templates").EnumerateArray());
    }

    [Fact]
    public async Task ImportAsync_RejectsDuplicateIdOrCode()
    {
        var root = CreateRoot(
            """
            {
              "templates": [
                {
                  "id": "existing",
                  "code": "EXISTING",
                  "name": "Existing",
                  "templateFilePath": "templates/existing.grb",
                  "outputFormats": ["pdf"]
                }
              ]
            }
            """);
        var service = CreateService(root);

        var result = await service.ImportAsync(
            CreateFormFile(
                "manifest.json",
                """
                {
                  "id": "existing",
                  "code": "OTHER",
                  "name": "Duplicate",
                  "outputFormats": ["pdf"]
                }
                """),
            CreateFormFile("Duplicate.grb", "t-flex"),
            null);

        Assert.False(result.IsSuccess);
        Assert.Contains("already exists", result.Errors["manifest"][0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportAsync_RejectsNullOrInvalidManifestCollectionsWithoutThrowing()
    {
        var root = CreateRoot();
        var service = CreateService(root);

        var result = await service.ImportAsync(
            CreateFormFile(
                "manifest.json",
                """
                {
                  "id": "invalid_definition",
                  "code": "INVALID-DEFINITION",
                  "name": "Invalid definition",
                  "outputFormats": ["pdf"],
                  "parameters": [
                    { "name": "AH", "type": "unsupported" }
                  ],
                  "calculatedVariables": [
                    { "name": "AREA", "type": "number", "expression": null }
                  ],
                  "validationRules": null,
                  "lookupTables": null
                }
                """),
            CreateFormFile("Invalid.grb", "t-flex"),
            null);

        Assert.False(result.IsSuccess);
        Assert.Contains("manifest.parameters[0].type", result.Errors.Keys);
        Assert.Contains("manifest.calculatedVariables[0].expression", result.Errors.Keys);
    }

    [Fact]
    public async Task ImportAsync_RejectsUnsupportedValidationRuleSeverity()
    {
        var root = CreateRoot();
        var service = CreateService(root);

        var result = await service.ImportAsync(
            CreateFormFile(
                "manifest.json",
                """
                {
                  "id": "invalid_rule_severity",
                  "code": "INVALID-RULE-SEVERITY",
                  "name": "Invalid rule severity",
                  "outputFormats": ["pdf"],
                  "validationRules": [
                    {
                      "name": "notice",
                      "expression": "0",
                      "message": "Notice",
                      "severity": "info"
                    }
                  ]
                }
                """),
            CreateFormFile("InvalidSeverity.grb", "t-flex"),
            null);

        Assert.False(result.IsSuccess);
        Assert.Contains("manifest.validationRules[0].severity", result.Errors.Keys);
        Assert.Contains(
            "error or warning",
            result.Errors["manifest.validationRules[0].severity"][0],
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_NormalizesSupportedValidationRuleSeverity()
    {
        var root = CreateRoot();
        var service = CreateService(root);

        var result = await service.ImportAsync(
            CreateFormFile(
                "manifest.json",
                """
                {
                  "id": "warning_rule_severity",
                  "code": "WARNING-RULE-SEVERITY",
                  "name": "Warning rule severity",
                  "outputFormats": ["pdf"],
                  "validationRules": [
                    {
                      "name": "notice",
                      "expression": "0",
                      "message": "Notice",
                      "severity": " WARNING "
                    }
                  ]
                }
                """),
            CreateFormFile("WarningSeverity.grb", "t-flex"),
            null);

        Assert.True(result.IsSuccess);
        Assert.Equal("warning", Assert.Single(result.Template!.ValidationRules).Severity);
    }

    [Fact]
    public async Task ImportAsync_RejectsTextTypeForRealTFlexVariable()
    {
        var root = CreateRoot();
        var service = CreateService(root);

        var result = await service.ImportAsync(
            CreateFormFile(
                "manifest.json",
                """
                {
                  "id": "mismatched_variable_type",
                  "code": "MISMATCHED-VARIABLE-TYPE",
                  "name": "Mismatched variable type",
                  "outputFormats": ["pdf"],
                  "parameters": [
                    { "name": "AH", "type": "string" }
                  ]
                }
                """),
            CreateFormFile("Mismatched.grb", "t-flex"),
            null);

        Assert.False(result.IsSuccess);
        Assert.Contains("manifest.parameters[0].type", result.Errors.Keys);
        Assert.Contains(
            "beginning with '$'",
            result.Errors["manifest.parameters[0].type"][0],
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_RejectsUnsupportedExpressionsBeforeEnablingTemplate()
    {
        var root = CreateRoot();
        var service = CreateService(root);

        var result = await service.ImportAsync(
            CreateFormFile(
                "manifest.json",
                """
                {
                  "id": "unsafe_expression",
                  "code": "UNSAFE-EXPRESSION",
                  "name": "Unsafe expression",
                  "outputFormats": ["pdf"],
                  "parameters": [
                    { "name": "AH", "type": "number" }
                  ],
                  "calculatedVariables": [
                    {
                      "name": "RESULT",
                      "type": "number",
                      "expression": "launch_process(AH)"
                    }
                  ],
                  "validationRules": [
                    {
                      "name": "RESULT_SAFE",
                      "expression": "RESULT > 0",
                      "message": "Result must be positive."
                    }
                  ]
                }
                """),
            CreateFormFile("Unsafe.grb", "t-flex"),
            null);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            "manifest.calculatedVariables[0].expression",
            result.Errors.Keys);
        Assert.False(Directory.Exists(Path.Combine(
            root,
            "templates",
            "imported",
            "unsafe_expression")));
        using var catalog = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(root, "templates", "templates.json")));
        Assert.Empty(catalog.RootElement.GetProperty("templates").EnumerateArray());
    }

    [Fact]
    public async Task ImportAsync_AcceptsSupportedLookupExpression()
    {
        var root = CreateRoot();
        var service = CreateService(root);

        var result = await service.ImportAsync(
            CreateFormFile(
                "manifest.json",
                """
                {
                  "id": "lookup_expression",
                  "code": "LOOKUP-EXPRESSION",
                  "name": "Lookup expression",
                  "outputFormats": ["pdf"],
                  "parameters": [
                    { "name": "AH", "type": "number" }
                  ],
                  "calculatedVariables": [
                    {
                      "name": "RESULT",
                      "type": "number",
                      "expression": "find(T.VALUE, T.KEY == AH)"
                    }
                  ],
                  "lookupTables": {
                    "T": [
                      { "KEY": 1, "VALUE": 2 }
                    ]
                  }
                }
                """),
            CreateFormFile("Lookup.grb", "t-flex"),
            null);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ImportAsync_DirectLookupValidationRuleHasRuntimeParity()
    {
        var root = CreateRoot();
        var service = CreateService(root);

        var import = await service.ImportAsync(
            CreateFormFile(
                "manifest.json",
                """
                {
                  "id": "direct_lookup_rule",
                  "code": "DIRECT-LOOKUP-RULE",
                  "name": "Direct lookup rule",
                  "outputFormats": ["pdf"],
                  "parameters": [
                    {
                      "name": "KEY",
                      "type": "integer",
                      "isRequired": true,
                      "defaultValue": 1
                    }
                  ],
                  "validationRules": [
                    {
                      "name": "lookup_matches",
                      "expression": "find(T.VALUE, T.KEY == KEY) == 10",
                      "message": "Lookup value is invalid."
                    }
                  ],
                  "lookupTables": {
                    "T": [
                      { "KEY": 1, "VALUE": 10 }
                    ]
                  }
                }
                """),
            CreateFormFile("DirectLookup.grb", "t-flex"),
            null);

        Assert.True(
            import.IsSuccess,
            string.Join(
                Environment.NewLine,
                import.Errors.SelectMany(error =>
                    error.Value.Select(message => $"{error.Key}: {message}"))));
        var template = Assert.IsType<TFlexDrawingService.Core.Models.DrawingTemplate>(
            import.Template);
        var validation = await new DrawingJobValidator(
            new InMemoryTemplateCatalog(template)).ValidateAsync(
            new CreateDrawingJobRequest
            {
                TemplateId = template.Id,
                OutputFormat = "pdf",
                Parameters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    """{ "KEY": 1 }""")!
            });

        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
    }

    [Fact]
    public async Task ImportAsync_RejectsLookupTableBeyondRowBudget()
    {
        var root = CreateRoot();
        var service = CreateService(root);
        var rows = Enumerable.Range(0, TemplateImportService.MaxLookupRowsPerTable + 1)
            .Select(index => new Dictionary<string, int>
            {
                ["KEY"] = index,
                ["VALUE"] = index
            })
            .ToArray();
        var manifestJson = JsonSerializer.Serialize(new
        {
            id = "oversized_lookup",
            code = "OVERSIZED-LOOKUP",
            name = "Oversized lookup",
            outputFormats = new[] { "pdf" },
            parameters = new[]
            {
                new { name = "AH", type = "number" }
            },
            calculatedVariables = new[]
            {
                new
                {
                    name = "RESULT",
                    type = "number",
                    expression = "find(T.VALUE, T.KEY == AH)"
                }
            },
            lookupTables = new Dictionary<string, object>
            {
                ["T"] = rows
            }
        });

        var result = await service.ImportAsync(
            CreateFormFile("manifest.json", manifestJson),
            CreateFormFile("Oversized.grb", "t-flex"),
            null);

        Assert.False(result.IsSuccess);
        Assert.Contains("manifest.lookupTables.T", result.Errors.Keys);
        Assert.Contains(
            TemplateImportService.MaxLookupRowsPerTable.ToString(),
            result.Errors["manifest.lookupTables.T"][0],
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_QuarantinesIncompleteEarlierImportAndRetries()
    {
        var root = CreateRoot();
        var service = CreateService(root);
        var orphanDirectory = Path.Combine(
            root,
            "templates",
            "imported",
            "recovered_template");
        Directory.CreateDirectory(orphanDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(orphanDirectory, "unfinished.grb"),
            "unfinished");

        var result = await service.ImportAsync(
            CreateFormFile(
                "manifest.json",
                """
                {
                  "id": "recovered_template",
                  "code": "RECOVERED-TEMPLATE",
                  "name": "Recovered template",
                  "outputFormats": ["pdf"]
                }
                """),
            CreateFormFile("Recovered.grb", "complete"),
            null);

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(Path.Combine(
            root,
            "templates",
            "imported",
            "recovered_template",
            "Recovered.grb")));
        var recoveryRoot = Path.Combine(
            root,
            "templates",
            "imported",
            ".recovered-orphans");
        var recoveredOrphan = Assert.Single(Directory.EnumerateDirectories(recoveryRoot));
        Assert.True(File.Exists(Path.Combine(recoveredOrphan, "unfinished.grb")));
    }

    private static TemplateImportService CreateService(string root)
    {
        return new TemplateImportService(Options.Create(new TemplateCatalogOptions
        {
            ProjectRootPath = root,
            ConfigPath = Path.Combine(root, "templates", "templates.json")
        }));
    }

    private static string CreateRoot(string catalogJson = """{ "templates": [] }""")
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path.Combine(root, "templates"));
        File.WriteAllText(Path.Combine(root, "templates", "templates.json"), catalogJson);
        return root;
    }

    private static IFormFile CreateFormFile(string fileName, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName);
    }

    private static IFormFile CreateZipFile(
        string fileName,
        params (string Path, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(
                    entry.Open(),
                    Encoding.UTF8,
                    leaveOpen: false);
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return new FormFile(stream, 0, stream.Length, "fragments", fileName);
    }
}
