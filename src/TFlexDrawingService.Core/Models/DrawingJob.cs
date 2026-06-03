namespace TFlexDrawingService.Core.Models;

public sealed class DrawingJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    public string TemplateId { get; set; } = string.Empty;

    public DrawingJobStatus Status { get; set; } = DrawingJobStatus.Pending;

    public string InputParametersJson { get; set; } = "{}";

    public string OutputFormat { get; set; } = "pdf";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public string? ErrorMessage { get; set; }

    public string? WorkingDirectory { get; set; }

    public List<GeneratedFile> ResultFiles { get; set; } = [];
}
