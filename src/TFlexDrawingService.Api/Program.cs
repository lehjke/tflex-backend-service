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
const string InlineGeneratedFileItemKey = "TFlexDrawingService.InlineGeneratedFile";
const long MaxTemplateImportRequestBodyBytes =
    TemplateImportService.MaxManifestBytes
    + TemplateImportService.MaxTemplateBytes
    + TemplateImportService.MaxFragmentsArchiveBytes
    + (2L * 1024 * 1024);

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    WriteIndented = false
};

var builder = WebApplication.CreateBuilder(args);
var securityOptions = builder.Configuration.GetSection("Security").Get<SecurityOptions>() ?? new SecurityOptions();
SecurityOptionsValidation.Validate(securityOptions, builder.Environment.IsDevelopment());

builder.Host.UseWindowsService(options => options.ServiceName = "TFlexDrawingService.Api");
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = Math.Max(
        securityOptions.MaxRequestBodyBytes,
        MaxTemplateImportRequestBodyBytes);
});

builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection("Security"));
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = MaxTemplateImportRequestBodyBytes;
});
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
builder.Services.AddSingleton<PricingCatalogStore>();
builder.Services.AddSingleton<TemplateImportService>();
builder.Services.AddSingleton<ServiceReadinessProbe>();
builder.Services.AddHttpClient();
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
        var isInlineGeneratedFile = context.Items.ContainsKey(InlineGeneratedFileItemKey);
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = isInlineGeneratedFile ? "SAMEORIGIN" : "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        if (context.Request.IsHttps)
        {
            headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        }

        headers["Content-Security-Policy"] = isInlineGeneratedFile
            ? "default-src 'none'; frame-ancestors 'self'"
            : "default-src 'self'; script-src 'self'; style-src 'self'; " +
              "font-src 'self'; img-src 'self' data:; " +
              "object-src 'self'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'";
        return Task.CompletedTask;
    });

    await next();
});

app.Use(async (context, next) =>
{
    var requestBodyLimit = IsTemplateImportRequest(context.Request)
        ? MaxTemplateImportRequestBodyBytes
        : securityOptions.MaxRequestBodyBytes;
    var maxRequestBodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
    if (maxRequestBodySizeFeature is { IsReadOnly: false })
    {
        maxRequestBodySizeFeature.MaxRequestBodySize = requestBodyLimit;
    }

    if (context.Request.ContentLength > requestBodyLimit)
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

app.MapGet("/drawings", (IWebHostEnvironment environment) => ServeHtml(environment, "drawings.html"));
app.MapGet("/pricing", (IWebHostEnvironment environment) => ServeHtml(environment, "pricing.html"));
app.MapGet("/account", (IWebHostEnvironment environment) => ServeHtml(environment, "account.html"));

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

var livenessHandler = () => Results.Ok(new
{
    Status = "ok",
    Time = DateTimeOffset.UtcNow
});

app.MapGet("/api/health", livenessHandler)
.AllowAnonymous();

app.MapGet("/api/health/live", livenessHandler)
.AllowAnonymous();

app.MapGet("/api/health/ready", async (
    ServiceReadinessProbe readinessProbe,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var readiness = await readinessProbe.CheckAsync(cancellationToken);
    return Results.Json(
        readiness,
        statusCode: readiness.Ready
            ? StatusCodes.Status200OK
            : StatusCodes.Status503ServiceUnavailable);
})
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
    var existing = await users.FindUserAsync(userName, cancellationToken);
    if (existing is null)
    {
        return Results.NotFound();
    }

    if (!string.Equals(existing.ApprovalStatus, "Pending", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(existing.ApprovalStatus, "Rejected", StringComparison.OrdinalIgnoreCase))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["approvalStatus"] = ["Only pending or rejected users can be approved."]
        });
    }

    return await users.ApproveUserAsync(
        userName,
        GetUserName(context.User),
        request.Roles,
        cancellationToken)
        ? Results.NoContent()
        : Results.Conflict(new
        {
            Message = "The user state changed before the approval was saved."
        });
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

    var existing = await users.FindUserAsync(userName, cancellationToken);
    if (existing is null)
    {
        return Results.NotFound();
    }

    if (existing.Enabled
        && IsApprovedUser(existing)
        && existing.Roles.Contains("Admin", StringComparer.OrdinalIgnoreCase))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["userName"] =
            [
                "An active admin cannot be rejected. Demote the user through the guarded edit operation first."
            ]
        });
    }

    return await users.RejectUserAsync(userName, GetUserName(context.User), cancellationToken)
        ? Results.NoContent()
        : Results.Conflict(new
        {
            Message = "The user state changed before the rejection was saved."
        });
});
RequirePolicy(adminRejectUserEndpoint, securityOptions.RequireAuthentication, AdminPolicy);

var adminUpsertUserEndpoint = app.MapPut("/api/admin/users/{userName}", async (
    string userName,
    UserUpsertRequest request,
    ConfiguredUserStore users,
    HttpContext context,
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

    var normalizedRoles = NormalizeRoles(request.Roles ?? existing?.Roles ?? ["Viewer"]);
    var enabled = request.Enabled ?? existing?.Enabled ?? true;
    var removesAdministrativeAccess = existing is not null
        && existing.Enabled
        && IsApprovedUser(existing)
        && existing.Roles.Any(role => string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
        && (!enabled || !normalizedRoles.Contains("Admin", StringComparer.OrdinalIgnoreCase));
    if (removesAdministrativeAccess)
    {
        var isSelf = string.Equals(
            normalizedUserName,
            GetUserName(context.User),
            StringComparison.OrdinalIgnoreCase);
        if (isSelf)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["roles"] =
                [
                    "You cannot disable or remove the Admin role from your own user."
                ]
            });
        }
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
        Enabled = enabled,
        ApprovalStatus = existing?.ApprovalStatus ?? "Approved",
        RequestedAt = existing?.RequestedAt,
        ApprovedAt = existing?.ApprovedAt,
        ApprovedByUserName = existing?.ApprovedByUserName,
        Roles = normalizedRoles.ToList()
    };

    var upsertResult = await users.TryUpsertUserPreservingLastAdminAsync(updated, cancellationToken);
    return upsertResult switch
    {
        ConfiguredUserUpsertResult.Succeeded => Results.Ok(ToPublicUserDto(updated)),
        ConfiguredUserUpsertResult.LastEnabledAdminWouldBeRemoved =>
            Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["roles"] = ["The last enabled admin user cannot be disabled or demoted."]
            }),
        ConfiguredUserUpsertResult.UserNameReserved => Results.Conflict(new
        {
            Message = "This user name is reserved and cannot be reused."
        }),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
    };
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
        return Results.Ok(templates.Select(ToPublicTemplateDto));
    }

    var states = await templateAccess.GetStatesAsync(cancellationToken);
    return Results.Ok(templates
        .Where(template => !states.TryGetValue(template.Id, out var enabled) || enabled)
        .Select(ToPublicTemplateDto));
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

    return template is null ? Results.NotFound() : Results.Ok(ToPublicTemplateDto(template));
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

var adminTemplateImportEndpoint = app.MapPost("/api/admin/templates/import", async (
    HttpRequest request,
    TemplateImportService templateImporter,
    TemplateAccessStore templateAccess,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["request"] = ["A multipart/form-data request is required."]
        });
    }

    IFormCollection form;
    try
    {
        form = await request.ReadFormAsync(cancellationToken);
    }
    catch (InvalidDataException)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["request"] = ["The multipart form is invalid or exceeds the configured limit."]
        });
    }
    catch (BadHttpRequestException)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["request"] = ["The multipart form could not be read."]
        });
    }
    catch (IOException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["request"] = ["The multipart upload was interrupted or could not be read."]
        });
    }

    var result = await templateImporter.ImportAsync(
        form.Files.GetFile("manifest"),
        form.Files.GetFile("template"),
        form.Files.GetFile("fragments"),
        cancellationToken);
    if (!result.IsSuccess || result.Template is null)
    {
        return Results.ValidationProblem(result.Errors.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase));
    }

    await templateAccess.SetEnabledAsync(
        result.Template.Id,
        enabled: true,
        GetUserName(context.User),
        cancellationToken);

    return Results.Created(
        $"/api/templates/{Uri.EscapeDataString(result.Template.Id)}",
        ToPublicTemplateDto(result.Template));
});
RequirePolicy(adminTemplateImportEndpoint, securityOptions.RequireAuthentication, AdminPolicy);

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
    IDrawingJobQueue queue,
    TemplateAccessStore templateAccess,
    IOptions<DrawingQueueOptions> queueOptions,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var ownerUserName = securityOptions.RequireAuthentication ? GetUserName(context.User) : "local";
    var queueLimits = queueOptions.Value;
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

    var enqueueResult = await queue.TryEnqueueAsync(
        job,
        queueLimits.MaxActiveJobs,
        queueLimits.MaxActiveJobsPerUser,
        cancellationToken);
    if (enqueueResult == DrawingJobEnqueueResult.TotalLimitReached)
    {
        return Results.Problem(
            title: "Queue is full.",
            detail: "Try again after current drawing jobs finish.",
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    if (enqueueResult == DrawingJobEnqueueResult.UserLimitReached)
    {
        return Results.Problem(
            title: "User queue limit reached.",
            detail: "Wait for one of your drawing jobs to finish before creating another.",
            statusCode: StatusCodes.Status429TooManyRequests);
    }

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
    bool? inline,
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

    var contentType = ContentTypeFor(file.Format);
    context.Response.Headers.CacheControl = "private, no-store";
    if (inline == true && string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
    {
        context.Items[InlineGeneratedFileItemKey] = true;
        return Results.File(
            file.Path,
            contentType,
            enableRangeProcessing: true);
    }

    return Results.File(
        file.Path,
        contentType,
        fileDownloadName: file.FileName,
        enableRangeProcessing: true);
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
RequirePolicy(createProjectEndpoint, securityOptions.RequireAuthentication, OperatorPolicy);

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
RequirePolicy(updateProjectEndpoint, securityOptions.RequireAuthentication, OperatorPolicy);

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
RequirePolicy(deleteProjectEndpoint, securityOptions.RequireAuthentication, OperatorPolicy);

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
    IDrawingRequestValidator validator,
    TemplateAccessStore templateAccess,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var validation = await validator.ValidateAsync(
        new CreateDrawingJobRequest
        {
            TemplateId = request.TemplateId,
            OutputFormat = request.OutputFormat,
            Parameters = request.Parameters ?? new Dictionary<string, JsonElement>()
        },
        cancellationToken);
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
            ["templateId"] = ["Template is disabled."]
        });
    }

    var configuration = await projects.SaveConfigurationAsync(
        GetProjectOwnerScope(context.User, securityOptions),
        projectId,
        request.Name,
        validation.Template.Id,
        validation.OutputFormat,
        ToJsonElements(validation.NormalizedParameters),
        cancellationToken);

    return configuration is null
        ? Results.NotFound()
        : Results.Created(
            $"/api/projects/{projectId}/configurations/{configuration.Id}",
            ToProjectConfigurationDto(configuration));
});
RequirePolicy(saveConfigurationEndpoint, securityOptions.RequireAuthentication, OperatorPolicy);

var updateConfigurationEndpoint = app.MapPut("/api/project-configurations/{configurationId}", async (
    string configurationId,
    ProjectConfigurationSaveRequest request,
    ProjectStore projects,
    IDrawingRequestValidator validator,
    TemplateAccessStore templateAccess,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var validation = await validator.ValidateAsync(
        new CreateDrawingJobRequest
        {
            TemplateId = request.TemplateId,
            OutputFormat = request.OutputFormat,
            Parameters = request.Parameters ?? new Dictionary<string, JsonElement>()
        },
        cancellationToken);
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
            ["templateId"] = ["Template is disabled."]
        });
    }

    var configuration = await projects.UpdateConfigurationAsync(
        GetProjectOwnerScope(context.User, securityOptions),
        configurationId,
        request.Name,
        validation.Template.Id,
        validation.OutputFormat,
        ToJsonElements(validation.NormalizedParameters),
        cancellationToken);

    return configuration is null
        ? Results.NotFound()
        : Results.Ok(ToProjectConfigurationDto(configuration));
});
RequirePolicy(updateConfigurationEndpoint, securityOptions.RequireAuthentication, OperatorPolicy);

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
RequirePolicy(deleteConfigurationEndpoint, securityOptions.RequireAuthentication, OperatorPolicy);

var pricingCatalogEndpoint = app.MapGet("/api/pricing/catalog", (
    PricingCatalogStore pricing) =>
{
    return Results.Ok(pricing.GetSummary());
});
RequirePolicy(pricingCatalogEndpoint, securityOptions.RequireAuthentication, ViewerPolicy);

var pricingCalculateEndpoint = app.MapPost("/api/pricing/calculate", async (
    PricingCalculationRequest? request,
    PricingCatalogStore pricing,
    CancellationToken cancellationToken) =>
{
    var validationErrors = ValidatePricingRequest(request);
    if (validationErrors.Count > 0)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["request"] = validationErrors.ToArray()
        });
    }

    return Results.Ok(await pricing.CalculateAsync(request!, cancellationToken));
});
RequirePolicy(pricingCalculateEndpoint, securityOptions.RequireAuthentication, ViewerPolicy);

var pricingSpecificationsEndpoint = app.MapGet("/api/projects/{projectId}/pricing-specifications", async (
    string projectId,
    ProjectStore projects,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var specifications = await projects.ListPricingSpecificationsAsync(
        projectId,
        GetProjectOwnerScope(context.User, securityOptions),
        cancellationToken);
    return Results.Ok(specifications.Select(ToPricingSpecificationDto));
});
RequirePolicy(pricingSpecificationsEndpoint, securityOptions.RequireAuthentication, ViewerPolicy);

var savePricingSpecificationEndpoint = app.MapPost("/api/projects/{projectId}/pricing-specifications", async (
    string projectId,
    PricingSpecificationSaveRequest? request,
    ProjectStore projects,
    PricingCatalogStore pricing,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var validationErrors = ValidatePricingRequest(request?.Request);
    if (validationErrors.Count > 0)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["request"] = validationErrors.ToArray()
        });
    }

    var pricingRequest = request!.Request!;
    if (!string.IsNullOrWhiteSpace(pricingRequest.ProjectConfigurationId))
    {
        var projectConfiguration = await projects.GetConfigurationAsync(
            pricingRequest.ProjectConfigurationId,
            GetProjectOwnerScope(context.User, securityOptions),
            cancellationToken);
        if (projectConfiguration is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["projectConfigurationId"] = ["Project configuration was not found."]
            });
        }

        if (!string.Equals(projectConfiguration.ProjectId, projectId, StringComparison.Ordinal))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["projectConfigurationId"] = ["Project configuration does not belong to the target project."]
            });
        }
    }

    var calculation = await pricing.CalculateAsync(pricingRequest, cancellationToken);
    if (calculation.Status == "blocked")
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["request"] = calculation.Blockers.ToArray()
        });
    }

    var name = string.IsNullOrWhiteSpace(request.Name)
        ? pricingRequest.Name ?? $"{calculation.Series} {pricingRequest.CapacityKg} кг"
        : request.Name;
    var specification = await projects.SavePricingSpecificationAsync(
        GetEffectiveUserName(context.User, securityOptions),
        projectId,
        pricingRequest.ProjectConfigurationId,
        name,
        pricingRequest,
        calculation,
        cancellationToken);

    return specification is null
        ? Results.NotFound()
        : Results.Created($"/api/pricing-specifications/{specification.Id}", ToPricingSpecificationDto(specification));
});
RequirePolicy(savePricingSpecificationEndpoint, securityOptions.RequireAuthentication, OperatorPolicy);

var pricingSpecificationEndpoint = app.MapGet("/api/pricing-specifications/{specificationId}", async (
    string specificationId,
    ProjectStore projects,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var specification = await projects.GetPricingSpecificationAsync(
        specificationId,
        GetProjectOwnerScope(context.User, securityOptions),
        cancellationToken);
    return specification is null ? Results.NotFound() : Results.Ok(ToPricingSpecificationDto(specification));
});
RequirePolicy(pricingSpecificationEndpoint, securityOptions.RequireAuthentication, ViewerPolicy);

var pricingTkpEndpoint = app.MapGet("/api/pricing-specifications/{specificationId}/tkp", async (
    string specificationId,
    ProjectStore projects,
    PricingCatalogStore pricing,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var specification = await projects.GetPricingSpecificationAsync(
        specificationId,
        GetProjectOwnerScope(context.User, securityOptions),
        cancellationToken);
    if (specification is null)
    {
        return Results.NotFound();
    }

    var project = await projects.GetProjectAsync(
        specification.ProjectId,
        GetProjectOwnerScope(context.User, securityOptions),
        cancellationToken);
    var bytes = pricing.BuildTkpDocx(specification, project);
    var fileName = $"{SanitizeFileName(specification.Name)}-tkp.docx";
    context.Response.Headers.CacheControl = "private, no-store";
    return Results.File(
        bytes,
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        fileName);
});
RequirePolicy(pricingTkpEndpoint, securityOptions.RequireAuthentication, ViewerPolicy);

app.Map("/api/{**path}", () => Results.Problem(
    statusCode: StatusCodes.Status404NotFound,
    title: "API endpoint not found."));

app.MapFallbackToFile("index.html");

app.Run();

static IResult ServeHtml(IWebHostEnvironment environment, string fileName)
{
    var webRootPath = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
    return Results.File(Path.Combine(webRootPath, fileName), "text/html");
}

static List<string> ValidatePricingRequest(PricingCalculationRequest? request)
{
    if (request is null)
    {
        return ["Request body is required."];
    }

    var errors = new List<string>();
    if (!PricingCatalogStore.IsSupportedSupplier(request.Supplier))
    {
        errors.Add("Supplier must be XIZI or SMEC.");
    }

    if (string.IsNullOrWhiteSpace(request.Series)
        || request.CapacityKg <= 0
        || request.Speed <= 0
        || request.Stops <= 0)
    {
        errors.Add("Series, capacity, speed and stops are required.");
    }

    if (request.DoorCount <= 0)
    {
        errors.Add("Door count must be greater than zero.");
    }

    return errors;
}

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

static object ToPublicTemplateDto(DrawingTemplate template)
{
    return new
    {
        template.Id,
        template.Code,
        template.Name,
        template.Description,
        template.OutputFormats,
        template.Parameters,
        template.CalculatedVariables,
        template.ValidationRules,
        template.LookupTables
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

static object ToPricingSpecificationDto(PricingSpecification specification)
{
    return new
    {
        specification.Id,
        specification.ProjectId,
        specification.ProjectConfigurationId,
        specification.Name,
        specification.Supplier,
        specification.Series,
        specification.Status,
        specification.TotalCny,
        specification.TargetCurrency,
        specification.TotalConverted,
        Request = JsonSerializer.Deserialize<PricingCalculationRequest>(
            specification.RequestJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        Calculation = JsonSerializer.Deserialize<PricingCalculationResult>(
            specification.CalculationJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        specification.CreatedAt,
        specification.UpdatedAt
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

static string SanitizeFileName(string value)
{
    var invalid = Path.GetInvalidFileNameChars();
    var chars = value
        .Select(character => invalid.Contains(character) ? '_' : character)
        .ToArray();
    var fileName = new string(chars).Trim();
    return string.IsNullOrWhiteSpace(fileName) ? "tkp" : fileName;
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

static bool IsTemplateImportRequest(HttpRequest request)
{
    return HttpMethods.IsPost(request.Method)
        && string.Equals(
            request.Path.Value,
            "/api/admin/templates/import",
            StringComparison.OrdinalIgnoreCase);
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

static IReadOnlyDictionary<string, JsonElement> ToJsonElements(
    IReadOnlyDictionary<string, object?> parameters)
{
    return parameters.ToDictionary(
        pair => pair.Key,
        pair => JsonSerializer.SerializeToElement(pair.Value),
        StringComparer.OrdinalIgnoreCase);
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

public sealed record PricingSpecificationSaveRequest(
    string? Name,
    PricingCalculationRequest? Request);
