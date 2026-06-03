namespace TFlexDrawingService.Core.Models;

public sealed class GeneratedFile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    public string JobId { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string Format { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public long SizeBytes { get; set; }
}
