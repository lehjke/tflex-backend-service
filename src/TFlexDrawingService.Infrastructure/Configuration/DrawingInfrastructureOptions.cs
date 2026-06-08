namespace TFlexDrawingService.Infrastructure.Configuration;

public sealed class DrawingStorageOptions
{
    public string RootPath { get; set; } = "storage";

    public string DatabasePath { get; set; } = "storage/drawings.db";
}

public sealed class TemplateCatalogOptions
{
    public string ProjectRootPath { get; set; } = Directory.GetCurrentDirectory();

    public string ConfigPath { get; set; } = "templates/templates.json";
}

public sealed class DrawingQueueOptions
{
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    public int MaxActiveJobs { get; set; } = 50;

    public int MaxActiveJobsPerUser { get; set; } = 5;
}

public sealed class DrawingCleanupOptions
{
    public bool Enabled { get; set; } = true;

    public int FinishedJobRetentionDays { get; set; } = 30;

    public int BatchSize { get; set; } = 100;

    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);
}

public sealed class TFlexAutomationOptions
{
    public string Mode { get; set; } = "ExternalProcess";

    public string? CommandPath { get; set; }

    public string Arguments { get; set; } = "\"{requestPath}\" \"{responsePath}\"";

    public int TimeoutSeconds { get; set; } = 600;

    public bool WriteParameterFile { get; set; } = true;
}
