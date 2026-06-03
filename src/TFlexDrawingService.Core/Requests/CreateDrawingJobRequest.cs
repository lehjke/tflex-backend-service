using System.Text.Json;

namespace TFlexDrawingService.Core.Requests;

public sealed class CreateDrawingJobRequest
{
    public string TemplateId { get; set; } = string.Empty;

    public string OutputFormat { get; set; } = "pdf";

    public Dictionary<string, JsonElement> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
