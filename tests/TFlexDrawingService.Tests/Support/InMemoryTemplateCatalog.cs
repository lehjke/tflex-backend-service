using TFlexDrawingService.Core.Abstractions;
using TFlexDrawingService.Core.Models;

namespace TFlexDrawingService.Tests.Support;

internal sealed class InMemoryTemplateCatalog(params DrawingTemplate[] templates) : ITemplateCatalog
{
    public Task<IReadOnlyList<DrawingTemplate>> ListAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<DrawingTemplate>>(templates);
    }

    public Task<DrawingTemplate?> GetByIdOrCodeAsync(string idOrCode, CancellationToken cancellationToken = default)
    {
        var template = templates.FirstOrDefault(item =>
            string.Equals(item.Id, idOrCode, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Code, idOrCode, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(template);
    }
}
