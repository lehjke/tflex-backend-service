using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Api.Data;
using TFlexDrawingService.Api.Security;
using TFlexDrawingService.Core.Abstractions;
using TFlexDrawingService.Core.Models;
using TFlexDrawingService.Core.Requests;
using TFlexDrawingService.Infrastructure.Configuration;

namespace TFlexDrawingService.Api;

public static class EngineerRequestEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapEngineerRequests(this WebApplication app, bool requireAuthentication)
    {
        var group = app.MapGroup("/api/engineer-requests");
        if (requireAuthentication) group.RequireAuthorization();

        group.MapGet("", async (EngineerRequestStore store, IDrawingJobRepository jobs, HttpContext context, CancellationToken ct) =>
        {
            var rows = await store.ListAsync(cancellationToken: ct);
            var visible = rows.Where(row => CanView(context.User, row, requireAuthentication)).ToArray();
            var response = new List<object>(visible.Length);
            foreach (var row in visible) response.Add(await ToDtoAsync(row, store, jobs, null,
                UserName(context.User, requireAuthentication), CanProcess(context.User, row, requireAuthentication), ct));
            return Results.Ok(response);
        });

        group.MapPost("", async (CreateEngineerRequest body, ProjectStore projects, EngineerRequestStore store,
            IDrawingRequestValidator validator, TemplateAccessStore access, HttpContext context, CancellationToken ct) =>
        {
            if (requireAuthentication && !context.User.IsInRole("Seller")) return Results.Forbid();
            var seller = UserName(context.User, requireAuthentication);
            if (string.IsNullOrWhiteSpace(body.ProjectId)) return Invalid("projectId", "Choose a project.");
            var project = await projects.GetProjectAsync(body.ProjectId, seller, ct);
            if (project is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(body.Description) || body.Description.Length > 4000) return Invalid("description", "Describe the request in 1–4000 characters.");
            var classification = await validator.ClassifyAsync(AsDrawingRequest(body.TemplateId, body.OutputFormat, body.Parameters), ct);
            if (!classification.IsValid || classification.Template is null || classification.OutputFormat is null)
                return Invalid("request", classification.HardErrors.ToArray());
            if (classification.OverridableDeviations.Count == 0) return Invalid("request", "This configuration is standard. Create a drawing directly.");
            if (requireAuthentication && !await access.IsEnabledAsync(classification.Template.Id, ct)) return Invalid("templateId", "Selected template is disabled.");
            var row = await store.CreateAsync(project.Id, seller, project.Name, body.Description.Trim(),
                classification.Template.Id, classification.OutputFormat,
                JsonSerializer.Serialize(classification.NormalizedParameters, JsonOptions), ct);
            return Results.Created($"/api/engineer-requests/{row.Id}", await ToDtoAsync(row, store, null, validator, seller, false, ct));
        });

        group.MapGet("/{id}", async (string id, EngineerRequestStore store, IDrawingJobRepository jobs,
            IDrawingRequestValidator validator, HttpContext context, CancellationToken ct) =>
        {
            var row = await store.GetAsync(id, ct);
            if (row is null || !CanView(context.User, row, requireAuthentication)) return Results.NotFound();
            var dto = await ToDtoAsync(row, store, jobs, validator, UserName(context.User, requireAuthentication),
                CanProcess(context.User, row, requireAuthentication), ct);
            await store.MarkReadAsync(id, UserName(context.User, requireAuthentication), ct);
            return Results.Ok(dto);
        });

        group.MapPost("/{id}/claim", async (string id, EngineerRequestStore store, HttpContext context, CancellationToken ct) =>
        {
            if (!CanEngineer(context.User, requireAuthentication)) return Results.Forbid();
            return await store.ClaimAsync(id, UserName(context.User, requireAuthentication), ct) ? Results.NoContent() : Results.Conflict();
        });

        group.MapPost("/{id}/comment", async (string id, RequestMessage body, EngineerRequestStore store, HttpContext context, CancellationToken ct) =>
        {
            var row = await store.GetAsync(id, ct);
            if (row is null || !CanComment(context.User, row, requireAuthentication)) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(body.Message) || body.Message.Length > 4000) return Invalid("message", "Comment must be 1–4000 characters.");
            var actor = UserName(context.User, requireAuthentication);
            if (!await store.AddCommentAsync(id, actor, body.Message.Trim(), ct)) return Results.Conflict();
            if (row.Status == EngineerRequestStatus.NeedsClarification && Same(actor, row.Seller)) await store.ResumeAsync(id, actor, ct);
            return Results.NoContent();
        });

        group.MapPost("/{id}/clarify", async (string id, RequestMessage body, EngineerRequestStore store, HttpContext context, CancellationToken ct) =>
        {
            var row = await store.GetAsync(id, ct);
            if (row is null || !CanProcess(context.User, row, requireAuthentication)) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(body.Message) || body.Message.Length > 4000) return Invalid("message", "Clarification is required.");
            var actor = UserName(context.User, requireAuthentication);
            return await store.ClarifyAsync(id, actor, body.Message.Trim(), ct, context.User.IsInRole("Admin")) ? Results.NoContent() : Results.Conflict();
        });

        group.MapPost("/{id}/reject", async (string id, RequestMessage body, EngineerRequestStore store, HttpContext context, CancellationToken ct) =>
        {
            var row = await store.GetAsync(id, ct);
            if (row is null || !CanProcess(context.User, row, requireAuthentication)) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(body.Message) || body.Message.Length > 4000) return Invalid("message", "Rejection reason is required.");
            var actor = UserName(context.User, requireAuthentication);
            return await store.RejectAsync(id, actor, body.Message.Trim(), ct, context.User.IsInRole("Admin")) ? Results.NoContent() : Results.Conflict();
        });

        group.MapPost("/{id}/cancel", async (string id, EngineerRequestStore store, IDrawingJobRepository jobs, HttpContext context, CancellationToken ct) =>
        {
            var row = await store.GetAsync(id, ct);
            if (row is null || (requireAuthentication && !Same(row.Seller, UserName(context.User, true)) && !context.User.IsInRole("Admin"))) return Results.NotFound();
            if (!await store.TransitionAsync(id, UserName(context.User, requireAuthentication), EngineerRequestStatus.Cancelled, ct)) return Results.Conflict();
            if (row.LinkedJobId is not null) await jobs.TryCancelPendingAsync(row.LinkedJobId, ct);
            return Results.NoContent();
        });

        group.MapPost("/{id}/parameters", async (string id, EngineerParameters body, EngineerRequestStore store,
            IDrawingRequestValidator validator, IDrawingJobRepository jobs, HttpContext context, CancellationToken ct) =>
        {
            var row = await store.GetAsync(id, ct);
            if (row is null || !CanProcess(context.User, row, requireAuthentication)) return Results.NotFound();
            var classified = await validator.ClassifyAsync(AsDrawingRequest(row.TemplateId, row.OutputFormat, body.Parameters), ct);
            if (!classified.IsValid) return Invalid("parameters", classified.HardErrors.ToArray());
            var actor = UserName(context.User, requireAuthentication);
            var isAdmin = context.User.IsInRole("Admin");
            if (row.LinkedJobId is not null)
            {
                var job = await jobs.GetAsync(row.LinkedJobId, ct);
                if (job is { Status: DrawingJobStatus.Running }
                    || job is { Status: DrawingJobStatus.Pending }
                        && !await jobs.TryCancelPendingAsync(row.LinkedJobId, ct))
                    return Results.Conflict(new { Message = "Wait for the drawing job to finish before changing parameters." });
                if (!await store.ClearLinkedJobAsync(id, actor, row.LinkedJobId, ct, isAdmin)) return Results.Conflict();
            }
            return await store.UpdateEngineerParametersAsync(id, UserName(context.User, requireAuthentication),
                JsonSerializer.Serialize(classified.NormalizedParameters, JsonOptions), ct, isAdmin) ? Results.NoContent() : Results.Conflict();
        });

        group.MapPost("/{id}/generate", async (string id, EngineerConfirmation body, EngineerRequestStore store,
            IDrawingRequestValidator validator, IDrawingJobQueue queue, IDrawingJobRepository jobs,
            TemplateAccessStore access, IOptions<DrawingQueueOptions> limits, HttpContext context, CancellationToken ct) =>
        {
            var row = await store.GetAsync(id, ct);
            if (row is null || !CanProcess(context.User, row, requireAuthentication)) return Results.NotFound();
            if (row.Status != EngineerRequestStatus.InProgress) return Results.Conflict();
            var parameters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                row.EngineerParametersJson == "{}" ? row.ParametersJson : row.EngineerParametersJson, JsonOptions) ?? [];
            var classified = await validator.ClassifyAsync(AsDrawingRequest(row.TemplateId, row.OutputFormat, parameters), ct);
            if (!classified.IsValid || classified.Template is null || classified.OutputFormat is null) return Invalid("parameters", classified.HardErrors.ToArray());
            if (requireAuthentication && !context.User.IsInRole("Admin")
                && !await access.IsEnabledAsync(classified.Template.Id, ct)) return Invalid("templateId", "Selected template is disabled.");
            if (classified.OverridableDeviations.Count > 0 && !Same(body.Fingerprint, classified.Fingerprint))
                return Results.Json(new { classified.OverridableDeviations, classified.Fingerprint }, statusCode: 409);
            if (row.LinkedJobId is not null)
            {
                var prior = await jobs.GetAsync(row.LinkedJobId, ct);
                if (prior is { Status: DrawingJobStatus.Pending or DrawingJobStatus.Running }
                    || prior is { Status: DrawingJobStatus.Completed }
                        && Same(row.ConfirmationFingerprint, classified.Fingerprint))
                    return Results.Conflict(new { Message = "The existing drawing job must finish or be reviewed first." });
                if (!await store.ClearLinkedJobAsync(id, UserName(context.User, requireAuthentication), row.LinkedJobId, ct, context.User.IsInRole("Admin"))) return Results.Conflict();
            }
            var job = new DrawingJob
            {
                TemplateId = classified.Template.Id, OutputFormat = classified.OutputFormat,
                InputParametersJson = JsonSerializer.Serialize(classified.NormalizedParameters, JsonOptions),
                OwnerUserName = UserName(context.User, requireAuthentication)
            };
            if (!await store.ReserveJobAsync(id, job.OwnerUserName, job.Id, classified.Fingerprint, ct, context.User.IsInRole("Admin"))) return Results.Conflict();
            DrawingJobEnqueueResult enqueue;
            try
            {
                enqueue = await queue.TryEnqueueAsync(job, limits.Value.MaxActiveJobs, limits.Value.MaxActiveJobsPerUser, ct);
            }
            catch
            {
                await store.ClearLinkedJobAsync(id, job.OwnerUserName, job.Id, CancellationToken.None, context.User.IsInRole("Admin"));
                throw;
            }
            if (enqueue != DrawingJobEnqueueResult.Enqueued)
            {
                await store.ClearLinkedJobAsync(id, job.OwnerUserName, job.Id, ct, context.User.IsInRole("Admin"));
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }
            if ((await store.GetAsync(id, ct))?.Status == EngineerRequestStatus.Cancelled)
            {
                await jobs.TryCancelPendingAsync(job.Id, ct);
                return Results.Conflict(new { Message = "The request was cancelled." });
            }
            return Results.Created($"/api/jobs/{job.Id}", new { job.Id, job.Status });
        });

        group.MapPost("/{id}/issue", async (string id, EngineerRequestStore store, IDrawingJobRepository jobs,
            IDrawingRequestValidator validator, HttpContext context, CancellationToken ct) =>
        {
            var row = await store.GetAsync(id, ct);
            if (row is null || !CanProcess(context.User, row, requireAuthentication)) return Results.NotFound();
            if (row.LinkedJobId is null) return Results.Conflict(new { Message = "Generate and review a drawing first." });
            var job = await jobs.GetAsync(row.LinkedJobId, ct);
            if (job is not { Status: DrawingJobStatus.Completed } || job.ResultFiles.Count == 0) return Results.Conflict(new { Message = "A completed drawing is required." });
            var parameters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                row.EngineerParametersJson == "{}" ? row.ParametersJson : row.EngineerParametersJson, JsonOptions) ?? [];
            var current = await validator.ClassifyAsync(AsDrawingRequest(row.TemplateId, row.OutputFormat, parameters), ct);
            if (!current.IsValid || !Same(row.ConfirmationFingerprint, current.Fingerprint))
                return Results.Conflict(new { Message = "Template or parameters changed. Review deviations and generate the drawing again." });
            var result = JsonSerializer.Serialize(new { jobId = job.Id, fileIds = job.ResultFiles.Select(file => file.Id).ToArray() }, JsonOptions);
            return await store.PublishResultAsync(id, UserName(context.User, requireAuthentication), job.Id, result, ct, context.User.IsInRole("Admin")) ? Results.NoContent() : Results.Conflict();
        });

        group.MapPost("/{id}/reassign", async (string id, ReassignEngineer body, EngineerRequestStore store,
            ConfiguredUserStore users, HttpContext context, CancellationToken ct) =>
        {
            if (requireAuthentication && !context.User.IsInRole("Admin")) return Results.Forbid();
            var engineer = await users.FindUserAsync(body.EngineerUserName, ct);
            if (engineer is null || !engineer.Enabled || !engineer.Roles.Contains("Engineer", StringComparer.OrdinalIgnoreCase)) return Invalid("engineerUserName", "Choose an active engineer.");
            return await store.ReassignAsync(id, UserName(context.User, requireAuthentication), engineer.UserName, ct) ? Results.NoContent() : Results.Conflict();
        });
    }

    private static async Task<object> ToDtoAsync(EngineerRequest row, EngineerRequestStore store,
        IDrawingJobRepository? jobs, IDrawingRequestValidator? validator, string viewer, bool canProcess, CancellationToken ct)
    {
        var job = row.LinkedJobId is null || jobs is null ? null : await jobs.GetAsync(row.LinkedJobId, ct);
        var parameters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            row.EngineerParametersJson == "{}" ? row.ParametersJson : row.EngineerParametersJson, JsonOptions) ?? [];
        var classified = validator is null ? null : await validator.ClassifyAsync(AsDrawingRequest(row.TemplateId, row.OutputFormat, parameters), ct);
        var showEngineerWork = canProcess || row.Status == EngineerRequestStatus.Ready;
        var comments = await store.GetCommentsAsync(row.Id, ct);
        var events = await store.GetEventsAsync(row.Id, ct);
        return new
        {
            row.Id, row.ProjectId, row.ProjectName, row.Description, row.TemplateId, row.OutputFormat,
            SellerUserName = row.Seller, EngineerUserName = row.Assignee, Status = row.Status.ToString(),
            OriginalParameters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.ParametersJson, JsonOptions),
            Parameters = showEngineerWork ? parameters : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.ParametersJson, JsonOptions),
            Deviations = classified?.OverridableDeviations, HardErrors = canProcess ? classified?.HardErrors : null,
            Fingerprint = canProcess ? classified?.Fingerprint : null,
            LinkedJobId = showEngineerWork ? row.LinkedJobId : null,
            JobStatus = showEngineerWork ? job?.Status.ToString() : null,
            CanIssue = canProcess && row.Status == EngineerRequestStatus.InProgress && job is { Status: DrawingJobStatus.Completed } && job.ResultFiles.Count > 0,
            ReviewFileUrl = canProcess && job is { Status: DrawingJobStatus.Completed } && job.ResultFiles.FirstOrDefault() is { } reviewFile
                ? $"/api/jobs/{job.Id}/files/{reviewFile.Id}/download" : null,
            IssuedFileUrl = row.Status == EngineerRequestStatus.Ready && job?.ResultFiles.FirstOrDefault() is { } file
                ? $"/api/jobs/{job.Id}/files/{file.Id}/download" : null,
            Comments = comments.Select(comment => new { userName = comment.Author, message = comment.Body, comment.CreatedAt }),
            Events = events.Select(item => new { item.Actor, item.Kind, item.CreatedAt }),
            IsUnread = !await store.IsReadAsync(row.Id, viewer, ct), row.CreatedAt, row.UpdatedAt
        };
    }

    private static CreateDrawingJobRequest AsDrawingRequest(string templateId, string format, Dictionary<string, JsonElement>? parameters) =>
        new() { TemplateId = templateId, OutputFormat = format, Parameters = parameters ?? [] };

    private static bool CanEngineer(ClaimsPrincipal user, bool requireAuthentication) => !requireAuthentication || user.IsInRole("Admin") || user.IsInRole("Engineer");
    private static bool CanView(ClaimsPrincipal user, EngineerRequest row, bool requireAuthentication) => !requireAuthentication || CanEngineer(user, true) || Same(row.Seller, UserName(user, true));
    private static bool CanProcess(ClaimsPrincipal user, EngineerRequest row, bool requireAuthentication) =>
        !requireAuthentication || user.IsInRole("Admin") || user.IsInRole("Engineer") && Same(row.Assignee, UserName(user, true));
    private static bool CanComment(ClaimsPrincipal user, EngineerRequest row, bool requireAuthentication) =>
        CanProcess(user, row, requireAuthentication) || Same(row.Seller, UserName(user, requireAuthentication));
    private static string UserName(ClaimsPrincipal user, bool requireAuthentication) => requireAuthentication ? user.Identity?.Name ?? "anonymous" : "local";
    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static IResult Invalid(string key, params string[] errors) => Results.ValidationProblem(new Dictionary<string, string[]> { [key] = errors });
}

public sealed record CreateEngineerRequest(string ProjectId, string Description, string TemplateId, string OutputFormat, Dictionary<string, JsonElement>? Parameters);
public sealed record RequestMessage(string Message);
public sealed record EngineerParameters(Dictionary<string, JsonElement>? Parameters);
public sealed record EngineerConfirmation(string? Fingerprint);
public sealed record ReassignEngineer(string EngineerUserName);
