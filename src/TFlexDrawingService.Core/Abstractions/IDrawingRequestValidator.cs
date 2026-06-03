using TFlexDrawingService.Core.Requests;
using TFlexDrawingService.Core.Services;

namespace TFlexDrawingService.Core.Abstractions;

public interface IDrawingRequestValidator
{
    Task<DrawingJobValidationResult> ValidateAsync(CreateDrawingJobRequest request, CancellationToken cancellationToken = default);
}
