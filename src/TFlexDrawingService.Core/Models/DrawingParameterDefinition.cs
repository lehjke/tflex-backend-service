using System.Text.Json;

namespace TFlexDrawingService.Core.Models;

public sealed class DrawingParameterDefinition
{
    public string Name { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Type { get; set; } = "string";

    public string? Unit { get; set; }

    public bool IsRequired { get; set; }

    public bool IsReadOnly { get; set; }

    public bool SubmitDefault { get; set; } = true;

    public bool SubmitWhenDisabled { get; set; }

    public string? LevelExpression { get; set; }

    public string? Expression { get; set; }

    public List<Dictionary<string, JsonElement>> LookupValues { get; set; } = [];

    public decimal? MinValue { get; set; }

    public decimal? MaxValue { get; set; }

    public JsonElement? DefaultValue { get; set; }

    public List<string> AllowedValues { get; set; } = [];

    public Dictionary<string, string> AllowedValueLabels { get; set; } = [];

    public string? Description { get; set; }

    public bool Multiline { get; set; }

    public int? Rows { get; set; }
}
