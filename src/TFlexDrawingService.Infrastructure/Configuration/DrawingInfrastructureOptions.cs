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
}

public sealed class TFlexAutomationOptions
{
    public string Mode { get; set; } = "ExternalProcess";

    public string? CommandPath { get; set; }

    public string Arguments { get; set; } = "\"{requestPath}\" \"{responsePath}\"";

    public int TimeoutSeconds { get; set; } = 600;

    public bool WriteParameterFile { get; set; } = true;
}
