namespace TFlexDrawingService.Api.Security;

public sealed class SecurityOptions
{
    public bool RequireAuthentication { get; set; } = true;

    public string CookieName { get; set; } = "TFlexDrawingService.Auth";

    public int SessionMinutes { get; set; } = 480;

    public long MaxRequestBodyBytes { get; set; } = 1_048_576;

    public bool RequireCsrfHeader { get; set; } = true;

    public bool RedactJobErrors { get; set; } = true;

    public bool ExposeWorkingDirectory { get; set; } = false;

    public int LoginRateLimitPermitLimit { get; set; } = 10;

    public int LoginRateLimitWindowSeconds { get; set; } = 60;

    public int JobCreateRateLimitPermitLimit { get; set; } = 10;

    public int JobCreateRateLimitWindowSeconds { get; set; } = 60;

    public List<ConfiguredUser> Users { get; set; } = [];
}

public sealed class ConfiguredUser
{
    public string UserName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string ApprovalStatus { get; set; } = "Approved";

    public DateTimeOffset? RequestedAt { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public string? ApprovedByUserName { get; set; }

    public List<string> Roles { get; set; } = [];
}
