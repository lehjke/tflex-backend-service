using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TFlexDrawingService.Core.Abstractions;
using TFlexDrawingService.Core.Services;
using TFlexDrawingService.Infrastructure.Automation;
using TFlexDrawingService.Infrastructure.Persistence;
using TFlexDrawingService.Infrastructure.Queue;
using TFlexDrawingService.Infrastructure.Services;
using TFlexDrawingService.Infrastructure.Storage;

namespace TFlexDrawingService.Infrastructure.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDrawingInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string contentRootPath)
    {
        var projectRoot = ResolvePath(contentRootPath, configuration["Paths:ProjectRoot"] ?? "../..");

        services.Configure<TemplateCatalogOptions>(options =>
        {
            options.ProjectRootPath = projectRoot;
            options.ConfigPath = ResolvePath(projectRoot, configuration["TemplateCatalog:ConfigPath"] ?? "templates/templates.json");
        });

        services.Configure<DrawingStorageOptions>(options =>
        {
            options.RootPath = ResolvePath(projectRoot, configuration["Storage:RootPath"] ?? "storage");
            options.DatabasePath = ResolvePath(projectRoot, configuration["Storage:DatabasePath"] ?? "storage/drawings.db");
        });

        services.Configure<DrawingQueueOptions>(options =>
        {
            if (double.TryParse(configuration["Queue:PollIntervalSeconds"], out var seconds) && seconds > 0)
            {
                options.PollInterval = TimeSpan.FromSeconds(seconds);
            }

            if (double.TryParse(configuration["Queue:LeaseDurationSeconds"], out var leaseDurationSeconds)
                && leaseDurationSeconds >= 10)
            {
                options.LeaseDuration = TimeSpan.FromSeconds(leaseDurationSeconds);
            }

            if (double.TryParse(
                    configuration["Queue:LeaseHeartbeatIntervalSeconds"],
                    out var leaseHeartbeatSeconds)
                && leaseHeartbeatSeconds > 0)
            {
                options.LeaseHeartbeatInterval = TimeSpan.FromSeconds(leaseHeartbeatSeconds);
            }

            if (options.LeaseHeartbeatInterval >= options.LeaseDuration / 2)
            {
                options.LeaseHeartbeatInterval = options.LeaseDuration / 3;
            }

            if (int.TryParse(configuration["Queue:MaxActiveJobs"], out var maxActiveJobs)
                && maxActiveJobs > 0)
            {
                options.MaxActiveJobs = maxActiveJobs;
            }

            if (int.TryParse(configuration["Queue:MaxActiveJobsPerUser"], out var maxActiveJobsPerUser)
                && maxActiveJobsPerUser > 0)
            {
                options.MaxActiveJobsPerUser = maxActiveJobsPerUser;
            }
        });

        services.Configure<DrawingCleanupOptions>(options =>
        {
            if (bool.TryParse(configuration["Cleanup:Enabled"], out var enabled))
            {
                options.Enabled = enabled;
            }

            if (int.TryParse(configuration["Cleanup:FinishedJobRetentionDays"], out var retentionDays)
                && retentionDays > 0)
            {
                options.FinishedJobRetentionDays = retentionDays;
            }

            if (int.TryParse(configuration["Cleanup:BatchSize"], out var batchSize)
                && batchSize > 0)
            {
                options.BatchSize = batchSize;
            }

            if (double.TryParse(configuration["Cleanup:IntervalHours"], out var intervalHours)
                && intervalHours > 0)
            {
                options.Interval = TimeSpan.FromHours(intervalHours);
            }
        });

        services.Configure<TFlexAutomationOptions>(options =>
        {
            options.Mode = configuration["TFlexAutomation:Mode"] ?? options.Mode;
            options.CommandPath = configuration["TFlexAutomation:CommandPath"];
            options.Arguments = configuration["TFlexAutomation:Arguments"] ?? options.Arguments;

            if (int.TryParse(configuration["TFlexAutomation:TimeoutSeconds"], out var timeoutSeconds)
                && timeoutSeconds > 0)
            {
                options.TimeoutSeconds = timeoutSeconds;
            }

            if (bool.TryParse(configuration["TFlexAutomation:WriteParameterFile"], out var writeParameterFile))
            {
                options.WriteParameterFile = writeParameterFile;
            }

            if (bool.TryParse(
                    configuration["TFlexAutomation:HealthCheckEnabled"],
                    out var healthCheckEnabled))
            {
                options.HealthCheckEnabled = healthCheckEnabled;
            }

            if (int.TryParse(
                    configuration["TFlexAutomation:HealthCheckIntervalSeconds"],
                    out var healthCheckIntervalSeconds)
                && healthCheckIntervalSeconds > 0)
            {
                options.HealthCheckIntervalSeconds = healthCheckIntervalSeconds;
            }

            if (int.TryParse(
                    configuration["TFlexAutomation:HealthCheckTimeoutSeconds"],
                    out var healthCheckTimeoutSeconds)
                && healthCheckTimeoutSeconds > 0)
            {
                options.HealthCheckTimeoutSeconds = healthCheckTimeoutSeconds;
            }
        });

        services.AddSingleton<ITemplateCatalog, JsonTemplateCatalog>();
        services.AddSingleton<IDrawingRequestValidator, DrawingJobValidator>();
        services.AddSingleton<IDrawingJobRepository, SqliteDrawingJobRepository>();
        services.AddSingleton<IDrawingJobQueue, SqliteDrawingJobQueue>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<TFlexAutomationExecutionGate>();
        services.AddSingleton<TFlexAutomationReadinessState>();
        services.AddSingleton<ITFlexAutomationHealthProbe, TFlexAutomationHealthProbe>();
        services.AddSingleton<ITFlexAutomationClient>(provider =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<TFlexAutomationOptions>>().Value;
            return options.Mode.Trim().ToLowerInvariant() switch
            {
                "mock" => ActivatorUtilities.CreateInstance<MockTFlexAutomationClient>(provider),
                "external" or "externalprocess" or "real" =>
                    ActivatorUtilities.CreateInstance<ExternalProcessTFlexAutomationClient>(provider),
                _ => throw new InvalidOperationException(
                    $"Unsupported T-FLEX automation mode '{options.Mode}'. Use 'ExternalProcess' or 'Mock'.")
            };
        });
        services.AddHostedService<StorageInitializationHostedService>();

        return services;
    }

    public static IServiceCollection AddDrawingGenerationWorker(this IServiceCollection services)
    {
        services.AddSingleton<DrawingJobProcessor>();
        services.AddHostedService<TFlexAutomationStartupHostedService>();
        services.AddHostedService<WorkerHeartbeatHostedService>();
        services.AddHostedService<DrawingGenerationBackgroundService>();
        services.AddHostedService<StorageCleanupHostedService>();
        return services;
    }

    private static string ResolvePath(string basePath, string path)
    {
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(basePath, path));
    }
}
