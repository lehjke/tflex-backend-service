using System.Text.Json;
using TFlexDrawingService.Core.Abstractions;
using TFlexDrawingService.Core.Models;
using TFlexDrawingService.Core.Requests;
using TFlexDrawingService.Infrastructure.Configuration;

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    WriteIndented = false
};

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(options => options.ServiceName = "TFlexDrawingService.Api");
builder.Services.AddDrawingInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/templates", async (ITemplateCatalog catalog, CancellationToken cancellationToken) =>
{
    var templates = await catalog.ListAsync(cancellationToken);
    return Results.Ok(templates);
});

app.MapGet("/api/templates/{id}", async (
    string id,
    ITemplateCatalog catalog,
    CancellationToken cancellationToken) =>
{
    var template = await catalog.GetByIdOrCodeAsync(id, cancellationToken);
    return template is null ? Results.NotFound() : Results.Ok(template);
});

app.MapPost("/api/jobs", async (
    CreateDrawingJobRequest request,
    IDrawingRequestValidator validator,
    IDrawingJobQueue queue,
    CancellationToken cancellationToken) =>
{
    var validation = await validator.ValidateAsync(request, cancellationToken);
    if (!validation.IsValid || validation.Template is null || validation.OutputFormat is null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["request"] = validation.Errors.ToArray()
        });
    }

    var job = new DrawingJob
    {
        TemplateId = validation.Template.Id,
        OutputFormat = validation.OutputFormat,
        Status = DrawingJobStatus.Pending,
        InputParametersJson = JsonSerializer.Serialize(validation.NormalizedParameters, jsonOptions),
        CreatedAt = DateTimeOffset.UtcNow
    };

    await queue.EnqueueAsync(job, cancellationToken);
    return Results.Created($"/api/jobs/{job.Id}", ToJobDto(job));
});

app.MapGet("/api/jobs", async (
    int? take,
    IDrawingJobRepository repository,
    CancellationToken cancellationToken) =>
{
    var jobs = await repository.ListAsync(take ?? 25, cancellationToken);
    return Results.Ok(jobs.Select(ToJobDto));
});

app.MapGet("/api/jobs/{id}", async (
    string id,
    IDrawingJobRepository repository,
    CancellationToken cancellationToken) =>
{
    var job = await repository.GetAsync(id, cancellationToken);
    return job is null ? Results.NotFound() : Results.Ok(ToJobDto(job));
});

app.MapGet("/api/jobs/{jobId}/files/{fileId}/download", async (
    string jobId,
    string fileId,
    IDrawingJobRepository repository,
    CancellationToken cancellationToken) =>
{
    var file = await repository.GetGeneratedFileAsync(jobId, fileId, cancellationToken);
    if (file is null || !File.Exists(file.Path))
    {
        return Results.NotFound();
    }

    return Results.File(file.Path, ContentTypeFor(file.Format), file.FileName);
});

app.MapFallbackToFile("index.html");

app.Run();

static object ToJobDto(DrawingJob job)
{
    return new
    {
        job.Id,
        job.TemplateId,
        Status = job.Status.ToString(),
        job.OutputFormat,
        job.CreatedAt,
        job.StartedAt,
        job.FinishedAt,
        job.ErrorMessage,
        job.WorkingDirectory,
        ResultFiles = job.ResultFiles.Select(file => new
        {
            file.Id,
            file.FileName,
            file.Format,
            file.CreatedAt,
            file.SizeBytes,
            DownloadUrl = $"/api/jobs/{job.Id}/files/{file.Id}/download"
        })
    };
}

static string ContentTypeFor(string format)
{
    return format.Trim().TrimStart('.').ToLowerInvariant() switch
    {
        "pdf" => "application/pdf",
        "dxf" => "application/dxf",
        "dwg" => "application/octet-stream",
        _ => "application/octet-stream"
    };
}
