using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Api.Data;
using TFlexDrawingService.Api.Security;
using TFlexDrawingService.Core.Abstractions;
using TFlexDrawingService.Core.Models;
using TFlexDrawingService.Core.Requests;
using TFlexDrawingService.Infrastructure.Configuration;

const string ViewerPolicy = "Viewer";
const string OperatorPolicy = "Operator";
const string AdminPolicy = "Admin";
const string JobCreateRateLimitPolicy = "job-create";
const string LoginRateLimitPolicy = "login";
const string CsrfHeaderName = "X-TFlex-Requested-With";

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    WriteIndented = false
};

var builder = WebApplication.CreateBuilder(args);
var securityOptions = builder.Configuration.GetSection("Security").Get<SecurityOptions>() ?? new SecurityOptions();

builder.Host.UseWindowsService(options => options.ServiceName = "TFlexDrawingService.Api");
builder.WebHost.ConfigureKestrel(options =>
{
    if (securityOptions.MaxRequestBodyBytes > 0)
    {
        options.Limits.MaxRequestBodySize = securityOptions.MaxRequestBodyBytes;
    }
});

builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection("Security"));
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});
builder.Services.AddSingleton<ConfiguredUserStore>();
builder.Services.AddSingleton<ProjectStore>();
builder.Services.AddSingleton<TemplateAccessStore>();
builder.Services.AddDrawingInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = securityOptions.CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(Math.Max(15, securityOptions.SessionMinutes));
        options.SlidingExpiration = true;
        options.LoginPath = "/";
        options.AccessDeniedPath = "/";
        options.Events.OnRedirectToLogin = context =>
        {
            if (IsApiRequest(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (IsApiRequest(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnValidatePrincipal = async context =>
        {
            var userName = context.Principal?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(userName))
            {
                await RejectCookieAsync(context);
                return;
            }

            var users = context.HttpContext.RequestServices.GetRequiredService<ConfiguredUserStore>();
            var user = await users.FindUserAsync(userName, context.HttpContext.RequestAborted);
            if (user is null || !user.Enabled || !IsApprovedUser(user))
            {
                await RejectCookieAsync(context);
                return;
            }

            if (context.Principal is not null && PrincipalMatchesConfiguredUser(context.Principal, user))
            {
                return;
            }

            context.ReplacePrincipal(CreatePrincipal(user));
            context.ShouldRenew = true;
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(ViewerPolicy, policy => policy.RequireRole("Admin", "Operator", "Viewer"));
    options.AddPolicy(OperatorPolicy, policy => policy.RequireRole("Admin", "Operator"));
    options.AddPolicy(AdminPolicy, policy => policy.RequireRole("Admin"));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(LoginRateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetClientPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = Math.Max(1, securityOptions.LoginRateLimitPermitLimit),
                QueueLimit = 0,
                Window = TimeSpan.FromSeconds(Math.Max(1, securityOptions.LoginRateLimitWindowSeconds))
            }));

    options.AddPolicy(JobCreateRateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetUserPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = Math.Max(1, securityOptions.JobCreateRateLimitPermitLimit),
                QueueLimit = 0,
                Window = TimeSpan.FromSeconds(Math.Max(1, securityOptions.JobCreateRateLimitWindowSeconds))
            }));
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var userStore = scope.ServiceProvider.GetRequiredService<ConfiguredUserStore>();
    var projectStore = scope.ServiceProvider.GetRequiredService<ProjectStore>();
    var templateAccessStore = scope.ServiceProvider.GetRequiredService<TemplateAccessStore>();
    var userCount = await userStore.InitializeAsync();
    await projectStore.InitializeAsync();
    await templateAccessStore.InitializeAsync();
    if (securityOptions.RequireAuthentication && userCount == 0)
    {
        throw new InvalidOperationException(
            "Security:RequireAuthentication is enabled, but no users exist in the security database. Configure Security:Users for the first bootstrap user.");
    }
}

app.UseForwardedHeaders();

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self' 'unsafe-eval'; style-src 'self' https://fonts.googleapis.com; " +
            "font-src 'self' https://fonts.gstatic.com; img-src 'self' data:; " +
            "object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'";
        return Task.CompletedTask;
    });

    await next();
});

app.Use(async (context, next) =>
{
    if (securityOptions.MaxRequestBodyBytes > 0
        && context.Request.ContentLength > securityOptions.MaxRequestBodyBytes)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        await Results.Problem(
            title: "Request body is too large.",
            statusCode: StatusCodes.Status413PayloadTooLarge).ExecuteAsync(context);
        return;
    }

    await next();
});

app.Use(async (context, next) =>
{
    if (securityOptions.RequireCsrfHeader
        && IsUnsafeApiRequest(context.Request)
        && !string.Equals(context.Request.Path.Value, "/api/auth/login", StringComparison.OrdinalIgnoreCase)
        && !context.Request.Headers.ContainsKey(CsrfHeaderName))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await Results.Problem(
            title: "Missing CSRF request header.",
            detail: $"Send {CsrfHeaderName}: fetch for state-changing API requests.",
            statusCode: StatusCodes.Status400BadRequest).ExecuteAsync(context);
        return;
    }

    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapPost("/api/auth/login", async (
    LoginRequest request,
    ConfiguredUserStore users,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var user = await users.ValidateCredentialsAsync(request.UserName, request.Password, cancellationToken);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    var principal = CreatePrincipal(user);
    await context.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        principal,
        new AuthenticationProperties
        {
            AllowRefresh = true,
            IsPersistent = false,
            IssuedUtc = DateTimeOffset.UtcNow
        });

    return Results.Ok(ToUserDto(principal, user));
})
.AllowAnonymous()
.RequireRateLimiting(LoginRateLimitPolicy);

app.MapPost("/api/auth/register", async (
    RegisterRequest request,
    ConfiguredUserStore users,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.UserName)
        || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["request"] = ["User name and password are required."]
        });
    }

    var result = await users.RegisterPendingUserAsync(
        request.UserName,
        request.DisplayName ?? request.UserName,
        request.Password,
        cancellationToken);

    return result.Created
        ? Results.Accepted($"/api/auth/register/{request.UserName}", new
        {
            Status = "Pending",
            Message = "Registration request was sent. An administrator must approve access."
        })
        : Results.Conflict(new
        {
            Status = result.Status,
            Message = "A user with this login already exists."
        });
})
.AllowAnonymous()
.RequireRateLimiting(LoginRateLimitPolicy);

var logoutEndpoint = app.MapPost("/api/auth/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.NoContent();
});
RequirePolicy(logoutEndpoint, securityOptions.RequireAuthentication, ViewerPolicy);

app.MapGet("/api/auth/me", async (
    HttpContext context,
    ConfiguredUserStore users,
    CancellationToken cancellationToken) =>
{
    if (!securityOptions.RequireAuthentication)
    {
        return Results.Ok(new
        {
            IsAuthenticated = true,
            UserName = "local",
            DisplayName = "Local",
            Roles = new[] { "Admin", "Operator", "Viewer" }
        });
    }

    if (context.User.Identity?.IsAuthenticated != true)
    {
        return Results.Ok(new
        {
            IsAuthenticated = false,
            UserName = "",
            DisplayName = "",
            Roles = Array.Empty<string>()
        });
    }

    var configuredUser = await users.FindUserAsync(GetUserName(context.User), cancellationToken);
    return Results.Ok(ToUserDto(context.User, configuredUser));
})
.AllowAnonymous();

app.MapGet("/api/health", () => Results.Ok(new
{
    Status = "ok",
    Time = DateTimeOffset.UtcNow
}))
.AllowAnonymous();

var adminUsersEndpoint = app.MapGet("/api/admin/users", async (
    ConfiguredUserStore users,
    CancellationToken cancellationToken) =>
{
    var allUsers = await users.ListUsersAsync(cancellationToken);
    return Results.Ok(allUsers.Select(ToPublicUserDto));
});
RequirePolicy(adminUsersEndpoint, securityOptions.RequireAuthentication, AdminPolicy);

var adminApproveUserEndpoint = app.MapPost("/api/admin/users/{userName}/approve", async (
    string userName,
    ApproveUserRequest request,
    ConfiguredUserStore users,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    return await users.ApproveUserAsync(
        userName,
        GetUserName(context.User),
        request.Roles,
        cancellationToken)
        ? Results.NoContent()
        : Results.NotFound();
});
RequirePolicy(adminApproveUserEndpoint, securityOptions.RequireAuthentication, AdminPolicy);

var adminRejectUserEndpoint = app.MapPost("/api/admin/users/{userName}/reject", async (
    string userName,
    ConfiguredUserStore users,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    if (string.Equals(userName, GetUserName(context.User), StringComparison.OrdinalIgnoreCase))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["userName"] = ["You cannot reject your own user."]
        });
    }

    return await users.RejectUserAsync(userName, GetUserName(context.User), cancellationToken)
        ? Results.NoContent()
        : Results.NotFound();
});
RequirePolicy(adminRejectUserEndpoint, securityOptions.RequireAuthentication, AdminPolicy);

var adminUpsertUserEndpoint = app.MapPut("/api/admin/users/{userName}", async (
    string userName,
    UserUpsertRequest request,
    ConfiguredUserStore users,
    CancellationToken cancellationToken) =>
{
    var normalizedUserName = userName.Trim();
    if (string.IsNullOrWhiteSpace(normalizedUserName))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["userName"] = ["User name is required."]
        });
    }

    var existing = await users.FindUserAsync(normalizedUserName, cancellationToken);
    if (existing is null && string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["password"] = ["Password is required for new users."]
        });
    }

    var updated = new ConfiguredUser
    {
        UserName = normalizedUserName,
        DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? normalizedUserName
            : request.DisplayName.Trim(),
        PasswordHash = string.IsNullOrWhiteSpace(request.Password)
            ? existing!.PasswordHash
            : PasswordHashing.HashPassword(request.Password),
        Enabled = request.Enabled ?? existing?.Enabled ?? true,
        Roles = NormalizeRoles(request.Roles ?? existing?.Roles ?? ["Viewer"]).ToList()
    };

    await users.UpsertUserAsync(updated, cancellationToken);
    return Results.Ok(ToPublicUserDto(updated));
});
RequirePolicy(adminUpsertUserEndpoint, securityOptions.RequireAuthentication, AdminPolicy);

var adminDeleteUserEndpoint = app.MapDelete("/api/admin/users/{userName}", async (
    string userName,
    ConfiguredUserStore users,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var normalizedUserName = userName.Trim();
    if (string.Equals(normalizedUserName, GetUserName(context.User), StringComparison.OrdinalIgnoreCase))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["userName"] = ["You cannot delete your own user."]
        });
    }

    var existing = await users.FindUserAsync(normalizedUserName, cancellationToken);
    if (existing is null)
    {
        return Results.NotFound();
    }

    if (existing.Roles.Any(role => string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["userName"] = ["You cannot delete another admin user."]
        });
    }

    return await users.DeleteUserAsync(normalizedUserName, cancellationToken)
        ? Results.NoContent()
        : Results.NotFound();
});
RequirePolicy(adminDeleteUserEndpoint, securityOptions.RequireAuthentication, AdminPolicy);

var templatesEndpoint = app.MapGet("/api/templates", async (
    ITemplateCatalog catalog,
    TemplateAccessStore templateAccess,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var templates = await catalog.ListAsync(cancellationToken);
    if (!securityOptions.RequireAuthentication || context.User.IsInRole("Admin"))
    {
        return Results.Ok(templates);
    }

    var states = await templateAccess.GetStatesAsync(cancellationToken);
    return Results.Ok(templates.Where(template =>
        !states.TryGetValue(template.Id, out var enabled) || enabled));
});
RequirePolicy(templatesEndpoint, securityOptions.RequireAuthentication, ViewerPolicy);

var templateEndpoint = app.MapGet("/api/templates/{id}", async (
    string id,
    ITemplateCatalog catalog,
    TemplateAccessStore templateAccess,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var template = await catalog.GetByIdOrCodeAsync(id, cancellationToken);
    if (template is not null
        && securityOptions.RequireAuthentication
        && !context.User.IsInRole("Admin")
        && !await templateAccess.IsEnabledAsync(template.Id, cancellationToken))
    {
        return Results.NotFound();
    }

    return template is null ? Results.NotFound() : Results.Ok(template);
});
RequirePolicy(templateEndpoint, securityOptions.RequireAuthentication, ViewerPolicy);

var adminTemplatesEndpoint = app.MapGet("/api/admin/templates", async (
    ITemplateCatalog catalog,
    TemplateAccessStore templateAccess,
    CancellationToken cancellationToken) =>
{
    var templates = await catalog.ListAsync(cancellationToken);
    var states = await templateAccess.GetStatesAsync(cancellationToken);
    return Results.Ok(templates.Select(template => new
    {
        template.Id,
        template.Code,
        template.Name,
        OutputFormats = template.OutputFormats,
        Enabled = !states.TryGetValue(template.Id, out var enabled) || enabled
    }));
});
RequirePolicy(adminTemplatesEndpoint, securityOptions.RequireAuthentication, AdminPolicy);

var adminTemplateEnabledEndpoint = app.MapPut("/api/admin/templates/{templateId}/enabled", async (
    string templateId,
    TemplateEnabledRequest request,
    ITemplateCatalog catalog,
    TemplateAccessStore templateAccess,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var template = await catalog.GetByIdOrCodeAsync(templateId, cancellationToken);
    if (template is null)
    {
        return Results.NotFound();
    }

    await templateAccess.SetEnabledAsync(template.Id, request.Enabled, GetUserName(context.User), cancellationToken);
    return Results.NoContent();
});
RequirePolicy(adminTemplateEnabledEndpoint, securityOptions.RequireAuthentication, AdminPolicy);

var createJobEndpoint = app.MapPost("/api/jobs", async (
    CreateDrawingJobRequest request,
    IDrawingRequestValidator validator,
    IDrawingJobRepository repository,
    IDrawingJobQueue queue,
    TemplateAccessStore templateAccess,
    IOptions<DrawingQueueOptions> queueOptions,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var ownerUserName = securityOptions.RequireAuthentication ? GetUserName(context.User) : "local";
    var queueLimits = queueOptions.Value;
    var activeTotal = await repository.CountActiveAsync(cancellationToken: cancellationToken);
    if (activeTotal >= queueLimits.MaxActiveJobs)
    {
        return Results.Problem(
            title: "Queue is full.",
            detail: "Try again after current drawing jobs finish.",
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    var activeForUser = await repository.CountActiveAsync(ownerUserName, cancellationToken);
    if (activeForUser >= queueLimits.MaxActiveJobsPerUser)
    {
        return Results.Problem(
            title: "User queue limit reached.",
            detail: "Wait for one of your drawing jobs to finish before creating another.",
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    var validation = await validator.ValidateAsync(request, cancellationToken);
    if (!validation.IsValid || validation.Template is null || validation.OutputFormat is null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["request"] = validation.Errors.ToArray()
        });
    }

    if (securityOptions.RequireAuthentication
        && !context.User.IsInRole("Admin")
        && !await templateAccess.IsEnabledAsync(validation.Template.Id, cancellationToken))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["request"] = ["Selected template is disabled."]
        });
    }

    var job = new DrawingJob
    {
        TemplateId = validation.Template.Id,
        OutputFormat = validation.OutputFormat,
        OwnerUserName = ownerUserName,
        Status = DrawingJobStatus.Pending,
        InputParametersJson = JsonSerializer.Serialize(validation.NormalizedParameters, jsonOptions),
        CreatedAt = DateTimeOffset.UtcNow
    };

    await queue.EnqueueAsync(job, cancellationToken);
    return Results.Created($"/api/jobs/{job.Id}", ToJobDto(job, context.User, securityOptions));
})
.RequireRateLimiting(JobCreateRateLimitPolicy);
RequirePolicy(createJobEndpoint, securityOptions.RequireAuthentication, OperatorPolicy);

var jobsEndpoint = app.MapGet("/api/jobs", async (
    int? take,
    IDrawingJobRepository repository,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var jobs = !securityOptions.RequireAuthentication || CanViewAllJobs(context.User)
        ? await repository.ListAsync(take ?? 25, cancellationToken)
        : await repository.ListAsync(take ?? 25, GetUserName(context.User), cancellationToken);

    return Results.Ok(jobs.Select(job => ToJobDto(job, context.User, securityOptions)));
});
RequirePolicy(jobsEndpoint, securityOptions.RequireAuthentication, ViewerPolicy);

var jobEndpoint = app.MapGet("/api/jobs/{id}", async (
    string id,
    IDrawingJobRepository repository,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var job = !securityOptions.RequireAuthentication || CanViewAllJobs(context.User)
        ? await repository.GetAsync(id, cancellationToken)
        : await repository.GetAsync(id, GetUserName(context.User), cancellationToken);

    return job is null
        ? Results.NotFound()
        : Results.Ok(ToJobDto(job, context.User, securityOptions));
});
RequirePolicy(jobEndpoint, securityOptions.RequireAuthentication, ViewerPolicy);

var downloadEndpoint = app.MapGet("/api/jobs/{jobId}/files/{fileId}/download", async (
    string jobId,
    string fileId,
    IDrawingJobRepository repository,
    IOptions<DrawingStorageOptions> storageOptions,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var job = !securityOptions.RequireAuthentication || CanViewAllJobs(context.User)
        ? await repository.GetAsync(jobId, cancellationToken)
        : await repository.GetAsync(jobId, GetUserName(context.User), cancellationToken);

    var file = job?.ResultFiles.FirstOrDefault(candidate =>
        string.Equals(candidate.Id, fileId, StringComparison.OrdinalIgnoreCase));
    if (file is null || !File.Exists(file.Path) || !IsAllowedGeneratedFilePath(file.Path, storageOptions.Value))
    {
        return Results.NotFound();
    }

    return Results.File(file.Path, ContentTypeFor(file.Format), file.FileName);
});
RequirePolicy(downloadEndpoint, securityOptions.RequireAuthentication, ViewerPolicy);

var projectsEndpoint = app.MapGet("/api/projects", async (
    ProjectStore projects,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await projects.ListProjectsAsync(
        GetProjectOwnerScope(context.User, securityOptions),
        cancellationToken));
});
RequirePolicy(projectsEndpoint, securityOptions.RequireAuthentication, ViewerPolicy);

var createProjectEndpoint = app.MapPost("/api/projects", async (
    ProjectCreateRequest request,
    ProjectStore projects,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["name"] = ["Project name is required."]
        });
    }

    var project = await projects.CreateProjectAsync(
        GetEffectiveUserName(context.User, securityOptions),
        request.Name,
        request.Address,
        request.FactoryRequestNumber,
        request.Description,
        cancellationToken);
    return Results.Created($"/api/projects/{project.Id}", project);
});
RequirePolicy(createProjectEndpoint, securityOptions.RequireAuthentication, ViewerPolicy);

var updateProjectEndpoint = app.MapPut("/api/projects/{projectId}", async (
    string projectId,
    ProjectUpdateRequest request,
    ProjectStore projects,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["name"] = ["Project name is required."]
        });
    }

    var project = await projects.UpdateProjectAsync(
        projectId,
        GetProjectOwnerScope(context.User, securityOptions),
        request.Name,
        request.Address,
        request.FactoryRequestNumber,
        request.Description,
        cancellationToken);

    return project is null
        ? Results.NotFound()
        : Results.Ok(project);
});
RequirePolicy(updateProjectEndpoint, securityOptions.RequireAuthentication, ViewerPolicy);

var deleteProjectEndpoint = app.MapDelete("/api/projects/{projectId}", async (
    string projectId,
    ProjectStore projects,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    return await projects.DeleteProjectAsync(
        projectId,
        GetProjectOwnerScope(context.User, securityOptions),
        cancellationToken)
        ? Results.NoContent()
        : Results.NotFound();
});
RequirePolicy(deleteProjectEndpoint, securityOptions.RequireAuthentication, ViewerPolicy);

var projectConfigurationsEndpoint = app.MapGet("/api/projects/{projectId}/configurations", async (
    string projectId,
    ProjectStore projects,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var configurations = await projects.ListConfigurationsAsync(
        projectId,
        GetProjectOwnerScope(context.User, securityOptions),
        cancellationToken);
    return Results.Ok(configurations.Select(ToProjectConfigurationDto));
});
RequirePolicy(projectConfigurationsEndpoint, securityOptions.RequireAuthentication, ViewerPolicy);

var projectConfigurationEndpoint = app.MapGet("/api/project-configurations/{configurationId}", async (
    string configurationId,
    ProjectStore projects,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var configuration = await projects.GetConfigurationAsync(
        configurationId,
        GetProjectOwnerScope(context.User, securityOptions),
        cancellationToken);

    return configuration is null
        ? Results.NotFound()
        : Results.Ok(ToProjectConfigurationDto(configuration));
});
RequirePolicy(projectConfigurationEndpoint, securityOptions.RequireAuthentication, ViewerPolicy);

var saveConfigurationEndpoint = app.MapPost("/api/projects/{projectId}/configurations", async (
    string projectId,
    ProjectConfigurationSaveRequest request,
    ProjectStore projects,
    ITemplateCatalog catalog,
    TemplateAccessStore templateAccess,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var template = await catalog.GetByIdOrCodeAsync(request.TemplateId, cancellationToken);
    if (template is null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["templateId"] = ["Template was not found."]
        });
    }

    if (!template.OutputFormats.Contains(request.OutputFormat, StringComparer.OrdinalIgnoreCase))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["outputFormat"] = ["Output format is not available for this template."]
        });
    }

    if (securityOptions.RequireAuthentication
        && !context.User.IsInRole("Admin")
        && !await templateAccess.IsEnabledAsync(template.Id, cancellationToken))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["templateId"] = ["Template is disabled."]
        });
    }

    var configuration = await projects.SaveConfigurationAsync(
        GetProjectOwnerScope(context.User, securityOptions),
        projectId,
        request.Name,
        template.Id,
        request.OutputFormat,
        request.Parameters ?? new Dictionary<string, JsonElement>(),
        cancellationToken);

    return configuration is null
        ? Results.NotFound()
        : Results.Created(
            $"/api/projects/{projectId}/configurations/{configuration.Id}",
            ToProjectConfigurationDto(configuration));
});
RequirePolicy(saveConfigurationEndpoint, securityOptions.RequireAuthentication, ViewerPolicy);

var updateConfigurationEndpoint = app.MapPut("/api/project-configurations/{configurationId}", async (
    string configurationId,
    ProjectConfigurationSaveRequest request,
    ProjectStore projects,
    ITemplateCatalog catalog,
    TemplateAccessStore templateAccess,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var template = await catalog.GetByIdOrCodeAsync(request.TemplateId, cancellationToken);
    if (template is null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["templateId"] = ["Template was not found."]
        });
    }

    if (!template.OutputFormats.Contains(request.OutputFormat, StringComparer.OrdinalIgnoreCase))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["outputFormat"] = ["Output format is not available for this template."]
        });
    }

    if (securityOptions.RequireAuthentication
        && !context.User.IsInRole("Admin")
        && !await templateAccess.IsEnabledAsync(template.Id, cancellationToken))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["templateId"] = ["Template is disabled."]
        });
    }

    var configuration = await projects.UpdateConfigurationAsync(
        GetProjectOwnerScope(context.User, securityOptions),
        configurationId,
        request.Name,
        template.Id,
        request.OutputFormat,
        request.Parameters ?? new Dictionary<string, JsonElement>(),
        cancellationToken);

    return configuration is null
        ? Results.NotFound()
        : Results.Ok(ToProjectConfigurationDto(configuration));
});
RequirePolicy(updateConfigurationEndpoint, securityOptions.RequireAuthentication, ViewerPolicy);

var deleteConfigurationEndpoint = app.MapDelete("/api/project-configurations/{configurationId}", async (
    string configurationId,
    ProjectStore projects,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    return await projects.DeleteConfigurationAsync(
        configurationId,
        GetProjectOwnerScope(context.User, securityOptions),
        cancellationToken)
        ? Results.NoContent()
        : Results.NotFound();
});
RequirePolicy(deleteConfigurationEndpoint, securityOptions.RequireAuthentication, ViewerPolicy);

app.MapFallbackToFile("index.html");

app.Run();

static RouteHandlerBuilder RequirePolicy(
    RouteHandlerBuilder builder,
    bool enabled,
    string policy)
{
    return enabled ? builder.RequireAuthorization(policy) : builder;
}

static async Task RejectCookieAsync(CookieValidatePrincipalContext context)
{
    context.RejectPrincipal();
    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
}

static ClaimsPrincipal CreatePrincipal(ConfiguredUser user)
{
    var claims = new List<Claim>
    {
        new(ClaimTypes.Name, user.UserName),
        new(ClaimTypes.NameIdentifier, user.UserName),
        new("display_name", string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName)
    };

    foreach (var role in NormalizeRoles(user.Roles))
    {
        claims.Add(new Claim(ClaimTypes.Role, role));
    }

    return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
}

static bool IsApprovedUser(ConfiguredUser user)
{
    return string.Equals(user.ApprovalStatus, "Approved", StringComparison.OrdinalIgnoreCase);
}

static bool PrincipalMatchesConfiguredUser(ClaimsPrincipal principal, ConfiguredUser user)
{
    if (!string.Equals(GetUserName(principal), user.UserName, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    var displayName = principal.FindFirstValue("display_name") ?? string.Empty;
    var expectedDisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName;
    if (!string.Equals(displayName, expectedDisplayName, StringComparison.Ordinal))
    {
        return false;
    }

    var principalRoles = principal
        .FindAll(ClaimTypes.Role)
        .Select(claim => claim.Value)
        .Order(StringComparer.OrdinalIgnoreCase);
    var userRoles = NormalizeRoles(user.Roles)
        .Order(StringComparer.OrdinalIgnoreCase);

    return principalRoles.SequenceEqual(userRoles, StringComparer.OrdinalIgnoreCase);
}

static object ToUserDto(ClaimsPrincipal principal, ConfiguredUser? configuredUser)
{
    var roles = principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value).Distinct().ToArray();
    return new
    {
        IsAuthenticated = principal.Identity?.IsAuthenticated == true,
        UserName = principal.Identity?.Name ?? configuredUser?.UserName ?? "",
        DisplayName = principal.FindFirstValue("display_name")
            ?? configuredUser?.DisplayName
            ?? configuredUser?.UserName
            ?? "",
        Roles = roles
    };
}

static object ToPublicUserDto(ConfiguredUser user)
{
    return new
    {
        user.UserName,
        DisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName,
        user.Enabled,
        ApprovalStatus = string.IsNullOrWhiteSpace(user.ApprovalStatus) ? "Approved" : user.ApprovalStatus,
        user.RequestedAt,
        user.ApprovedAt,
        user.ApprovedByUserName,
        Roles = NormalizeRoles(user.Roles)
    };
}

static object ToProjectConfigurationDto(ProjectConfiguration configuration)
{
    return new
    {
        configuration.Id,
        configuration.ProjectId,
        configuration.OwnerUserName,
        configuration.Name,
        configuration.TemplateId,
        configuration.OutputFormat,
        Parameters = DeserializeParameters(configuration.ParametersJson),
        configuration.CreatedAt,
        configuration.UpdatedAt
    };
}

static object ToJobDto(DrawingJob job, ClaimsPrincipal user, SecurityOptions securityOptions)
{
    var canViewInternal = !securityOptions.RequireAuthentication || user.IsInRole("Admin");
    return new
    {
        job.Id,
        job.TemplateId,
        Status = job.Status.ToString(),
        job.OutputFormat,
        OwnerUserName = canViewInternal ? job.OwnerUserName : null,
        job.CreatedAt,
        job.StartedAt,
        job.FinishedAt,
        ErrorMessage = RedactError(job, securityOptions, canViewInternal),
        WorkingDirectory = securityOptions.ExposeWorkingDirectory && canViewInternal ? job.WorkingDirectory : null,
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

static string? RedactError(DrawingJob job, SecurityOptions securityOptions, bool canViewInternal)
{
    if (string.IsNullOrWhiteSpace(job.ErrorMessage))
    {
        return null;
    }

    if (!securityOptions.RedactJobErrors || canViewInternal)
    {
        return job.ErrorMessage;
    }

    return "Generation failed. Contact an administrator with the job ID.";
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

static string GetUserName(ClaimsPrincipal principal)
{
    return principal.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(principal.Identity.Name)
        ? principal.Identity.Name
        : "anonymous";
}

static string GetEffectiveUserName(ClaimsPrincipal principal, SecurityOptions securityOptions)
{
    return securityOptions.RequireAuthentication ? GetUserName(principal) : "local";
}

static string? GetProjectOwnerScope(ClaimsPrincipal principal, SecurityOptions securityOptions)
{
    return CanManageAllProjects(principal, securityOptions)
        ? null
        : GetEffectiveUserName(principal, securityOptions);
}

static bool CanManageAllProjects(ClaimsPrincipal principal, SecurityOptions securityOptions)
{
    return !securityOptions.RequireAuthentication || principal.IsInRole("Admin");
}

static bool CanViewAllJobs(ClaimsPrincipal principal)
{
    return principal.IsInRole("Admin");
}

static IReadOnlyList<string> NormalizeRoles(IEnumerable<string> roles)
{
    var normalized = roles
        .Select(role => role.Trim())
        .Where(role => !string.IsNullOrWhiteSpace(role))
        .Select(role => role.ToLowerInvariant() switch
        {
            "admin" => "Admin",
            "operator" => "Operator",
            "viewer" => "Viewer",
            _ => role
        })
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    return normalized.Length == 0 ? ["Viewer"] : normalized;
}

static string GetUserPartitionKey(HttpContext context)
{
    return context.User.Identity?.IsAuthenticated == true
        ? context.User.Identity.Name ?? GetClientPartitionKey(context)
        : GetClientPartitionKey(context);
}

static string GetClientPartitionKey(HttpContext context)
{
    return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

static bool IsApiRequest(HttpRequest request)
{
    return request.Path.StartsWithSegments("/api");
}

static bool IsUnsafeApiRequest(HttpRequest request)
{
    if (!IsApiRequest(request))
    {
        return false;
    }

    return HttpMethods.IsPost(request.Method)
        || HttpMethods.IsPut(request.Method)
        || HttpMethods.IsPatch(request.Method)
        || HttpMethods.IsDelete(request.Method);
}

static bool IsAllowedGeneratedFilePath(string path, DrawingStorageOptions storageOptions)
{
    var generatedRoot = Path.GetFullPath(Path.Combine(storageOptions.RootPath, "generated"));
    var fullPath = Path.GetFullPath(path);
    var comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    return string.Equals(fullPath, generatedRoot, comparison)
        || fullPath.StartsWith(
            generatedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar,
            comparison);
}

static IReadOnlyDictionary<string, JsonElement> DeserializeParameters(string parametersJson)
{
    return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            parametersJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? new Dictionary<string, JsonElement>();
}

public sealed record LoginRequest(string UserName, string Password);

public sealed record RegisterRequest(string UserName, string? DisplayName, string Password);

public sealed record ApproveUserRequest(IReadOnlyList<string>? Roles);

public sealed record UserUpsertRequest(
    string? DisplayName,
    string? Password,
    bool? Enabled,
    IReadOnlyList<string>? Roles);

public sealed record TemplateEnabledRequest(bool Enabled);

public sealed record ProjectCreateRequest(
    string Name,
    string? Address,
    string? FactoryRequestNumber,
    string? Description);

public sealed record ProjectUpdateRequest(
    string Name,
    string? Address,
    string? FactoryRequestNumber,
    string? Description);

public sealed record ProjectConfigurationSaveRequest(
    string Name,
    string TemplateId,
    string OutputFormat,
    Dictionary<string, JsonElement>? Parameters);
