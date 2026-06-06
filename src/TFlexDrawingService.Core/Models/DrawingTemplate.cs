using System.Text.Json;

namespace TFlexDrawingService.Core.Models;

public sealed class DrawingTemplate
{
    public string Id { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string TemplateFilePath { get; set; } = string.Empty;

    public List<string> OutputFormats { get; set; } = [];

    public List<DrawingParameterDefinition> Parameters { get; set; } = [];

    public List<DrawingParameterDefinition> CalculatedVariables { get; set; } = [];

    public List<DrawingValidationRule> ValidationRules { get; set; } = [];

    public Dictionary<string, List<Dictionary<string, JsonElement>>> LookupTables { get; set; } = [];
}

public sealed class DrawingValidationRule
{
    public string Name { get; set; } = string.Empty;

    public string Expression { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public List<string> FieldNames { get; set; } = [];
}
