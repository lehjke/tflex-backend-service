using TFlexDrawingService.Core.Models;

namespace TFlexDrawingService.Core.Services;

public sealed class DrawingJobValidationResult
{
    private DrawingJobValidationResult(
        bool isValid,
        DrawingTemplate? template,
        string? outputFormat,
        IReadOnlyDictionary<string, object?> normalizedParameters,
        IReadOnlyList<string> errors)
    {
        IsValid = isValid;
        Template = template;
        OutputFormat = outputFormat;
        NormalizedParameters = normalizedParameters;
        Errors = errors;
    }

    public bool IsValid { get; }

    public DrawingTemplate? Template { get; }

    public string? OutputFormat { get; }

    public IReadOnlyDictionary<string, object?> NormalizedParameters { get; }

    public IReadOnlyList<string> Errors { get; }

    public static DrawingJobValidationResult Success(
        DrawingTemplate template,
        string outputFormat,
        IReadOnlyDictionary<string, object?> normalizedParameters)
    {
        return new DrawingJobValidationResult(true, template, outputFormat, normalizedParameters, []);
    }

    public static DrawingJobValidationResult Failure(IReadOnlyList<string> errors)
    {
        return new DrawingJobValidationResult(false, null, null, new Dictionary<string, object?>(), errors);
    }
}
