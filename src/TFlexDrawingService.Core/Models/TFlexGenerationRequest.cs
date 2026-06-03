namespace TFlexDrawingService.Core.Models;

public sealed record TFlexGenerationRequest(
    DrawingJob Job,
    DrawingTemplate Template,
    string WorkingDirectory,
    string TemplateCopyPath,
    string ResultDirectory,
    IReadOnlyDictionary<string, object?> Parameters,
    string OutputFormat);
