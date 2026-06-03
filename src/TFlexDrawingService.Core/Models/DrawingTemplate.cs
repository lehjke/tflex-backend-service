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
}
