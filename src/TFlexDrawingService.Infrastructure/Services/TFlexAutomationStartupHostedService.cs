using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Infrastructure.Automation;
using TFlexDrawingService.Infrastructure.Configuration;

namespace TFlexDrawingService.Infrastructure.Services;

public sealed class TFlexAutomationStartupHostedService(
    IOptions<TFlexAutomationOptions> options,
    IHostEnvironment environment,
    ITFlexAutomationHealthProbe healthProbe,
    TFlexAutomationReadinessState readinessState,
    ILogger<TFlexAutomationStartupHostedService> logger) : IHostedService
{
    private readonly TFlexAutomationOptions _options = options.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var mode = (_options.Mode ?? string.Empty).Trim();
        var isMock = string.Equals(mode, "mock", StringComparison.OrdinalIgnoreCase);
        var isExternal = IsExternalMode(mode);

        if (!isMock && !isExternal)
        {
            throw new InvalidOperationException(
                $"Unsupported T-FLEX automation mode '{mode}'.");
        }

        if (isMock && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Mock T-FLEX automation is allowed only in the Development environment.");
        }

        if (isExternal
            && (string.IsNullOrWhiteSpace(_options.CommandPath)
                || !File.Exists(_options.CommandPath)))
        {
            throw new InvalidOperationException(
                "External T-FLEX automation runner was not found.");
        }

        if (!_options.HealthCheckEnabled)
        {
            readinessState.Update(
                TFlexAutomationHealthResult.Fail(
                    "Automation health-check is disabled."),
                DateTimeOffset.UtcNow,
                TimeSpan.Zero);
            logger.LogWarning(
                "T-FLEX automation health-check is disabled. "
                + "The Worker will stay not-ready and will not dequeue jobs.");
            return;
        }

        var checkedAt = DateTimeOffset.UtcNow;
        var result = await healthProbe.CheckAsync(cancellationToken);
        readinessState.Update(result, checkedAt, GetHealthValidity(_options));
        if (!result.Ready)
        {
            throw new InvalidOperationException(
                $"T-FLEX automation startup health-check failed: {result.Detail}");
        }

        logger.LogInformation(
            "T-FLEX automation startup health-check completed successfully.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private static bool IsExternalMode(string mode)
    {
        return mode.Equals("external", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("externalprocess", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("real", StringComparison.OrdinalIgnoreCase);
    }

    internal static TimeSpan GetHealthValidity(TFlexAutomationOptions options)
    {
        var interval = TimeSpan.FromSeconds(
            Math.Clamp(options.HealthCheckIntervalSeconds, 5, 86_400));
        var timeout = TimeSpan.FromSeconds(
            Math.Clamp(options.HealthCheckTimeoutSeconds, 1, 3_600));
        return interval + interval + timeout;
    }
}
