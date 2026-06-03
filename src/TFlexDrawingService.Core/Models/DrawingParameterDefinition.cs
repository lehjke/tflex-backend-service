using System.Text.Json;

namespace TFlexDrawingService.Core.Models;

public sealed class DrawingParameterDefinition
{
    public string Name { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Type { get; set; } = "string";

    public string? Unit { get; set; }

    public bool IsRequired { get; set; }

    public decimal? MinValue { get; set; }

    public decimal? MaxValue { get; set; }

    public JsonElement? DefaultValue { get; set; }

    public List<string> AllowedValues { get; set; } = [];

    public string? Description { get; set; }
}
