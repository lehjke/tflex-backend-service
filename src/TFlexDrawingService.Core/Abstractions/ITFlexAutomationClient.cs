using TFlexDrawingService.Core.Models;

namespace TFlexDrawingService.Core.Abstractions;

public interface ITFlexAutomationClient
{
    Task<IReadOnlyList<GeneratedFile>> GenerateAsync(TFlexGenerationRequest request, CancellationToken cancellationToken = default);
}
