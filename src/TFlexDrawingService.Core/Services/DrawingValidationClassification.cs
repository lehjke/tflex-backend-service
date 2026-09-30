using TFlexDrawingService.Core.Models;

namespace TFlexDrawingService.Core.Services;

public sealed record DrawingValidationClassification(
    IReadOnlyList<string> HardErrors,
    IReadOnlyList<string> OverridableDeviations,
    string? Fingerprint,
    DrawingTemplate? Template = null,
    string? OutputFormat = null,
    IReadOnlyDictionary<string, object?>? ParameterValues = null)
{
    public bool IsValid => HardErrors.Count == 0;

    public IReadOnlyDictionary<string, object?> NormalizedParameters => ParameterValues ?? new Dictionary<string, object?>();
}
