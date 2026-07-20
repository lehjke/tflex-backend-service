namespace TFlexDrawingService.Api.Security;

public static class SecurityOptionsValidation
{
    public const long MaximumRequestBodyBytes = 16L * 1024 * 1024;
    public const int MaximumRateLimitPermits = 100;
    public const int MaximumRateLimitWindowSeconds = 3600;
    public const int MaximumSessionMinutes = 1440;

    public static void Validate(SecurityOptions options, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!isDevelopment)
        {
            if (!options.RequireAuthentication)
            {
                throw new InvalidOperationException(
                    "Security:RequireAuthentication can be disabled only in Development.");
            }

            if (!options.RequireCsrfHeader)
            {
                throw new InvalidOperationException(
                    "Security:RequireCsrfHeader can be disabled only in Development.");
            }

            if (!options.RedactJobErrors || options.ExposeWorkingDirectory)
            {
                throw new InvalidOperationException(
                    "Production must redact job errors and hide Worker working directories.");
            }
        }

        if (string.IsNullOrWhiteSpace(options.CookieName) || options.CookieName.Length > 128)
        {
            throw new InvalidOperationException(
                "Security:CookieName is required and must not exceed 128 characters.");
        }

        ValidateRange(
            "Security:SessionMinutes",
            options.SessionMinutes,
            15,
            MaximumSessionMinutes);

        if (options.MaxRequestBodyBytes is <= 0 or > MaximumRequestBodyBytes)
        {
            throw new InvalidOperationException(
                $"Security:MaxRequestBodyBytes must be between 1 and {MaximumRequestBodyBytes}.");
        }

        ValidateRange(
            "Security:LoginRateLimitPermitLimit",
            options.LoginRateLimitPermitLimit,
            1,
            MaximumRateLimitPermits);
        ValidateRange(
            "Security:LoginRateLimitWindowSeconds",
            options.LoginRateLimitWindowSeconds,
            10,
            MaximumRateLimitWindowSeconds);
        ValidateRange(
            "Security:JobCreateRateLimitPermitLimit",
            options.JobCreateRateLimitPermitLimit,
            1,
            MaximumRateLimitPermits);
        ValidateRange(
            "Security:JobCreateRateLimitWindowSeconds",
            options.JobCreateRateLimitWindowSeconds,
            1,
            MaximumRateLimitWindowSeconds);
    }

    private static void ValidateRange(string name, int value, int minimum, int maximum)
    {
        if (value < minimum || value > maximum)
        {
            throw new InvalidOperationException(
                $"{name} must be between {minimum} and {maximum}.");
        }
    }
}
