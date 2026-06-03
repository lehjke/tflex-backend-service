using TFlexDrawingService.Core.Models;

namespace TFlexDrawingService.Core.Abstractions;

public interface ITemplateCatalog
{
    Task<IReadOnlyList<DrawingTemplate>> ListAsync(CancellationToken cancellationToken = default);

    Task<DrawingTemplate?> GetByIdOrCodeAsync(string idOrCode, CancellationToken cancellationToken = default);
}
