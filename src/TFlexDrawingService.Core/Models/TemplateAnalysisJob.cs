namespace TFlexDrawingService.Core.Models;

public enum TemplateAnalysisStatus
{
    Pending,
    Processing,
    Completed,
    Failed,
    Published
}

public sealed class TemplateAnalysisJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    public TemplateAnalysisStatus Status { get; set; } = TemplateAnalysisStatus.Pending;

    public string OwnerUserName { get; set; } = "legacy";

    public string OriginalTemplateFileName { get; set; } = string.Empty;

    public string TemplateFilePath { get; set; } = string.Empty;

    public string ComponentsDirectoryPath { get; set; } = string.Empty;

    public string DraftJson { get; set; } = string.Empty;

    public string InspectionJson { get; set; } = string.Empty;

    public string WarningsJson { get; set; } = "[]";

    public string ComponentManifestJson { get; set; } = "[]";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public string? ErrorMessage { get; set; }
}

public sealed class TFlexTemplateInspection
{
    public List<TFlexVariableInspection> Variables { get; set; } = [];

    public List<TFlexControlInspection> Controls { get; set; } = [];

    public List<TFlexDocumentDependency> Dependencies { get; set; } = [];

    public List<string> RebuildWarnings { get; set; } = [];
}

public sealed class TFlexDocumentDependency
{
    public string? ObjectName { get; set; }

    public string? ObjectType { get; set; }

    public string? Property { get; set; }

    public string? Value { get; set; }
}

public sealed class TFlexVariableInspection
{
    public string Name { get; set; } = string.Empty;

    public string? Expression { get; set; }

    public string? Value { get; set; }

    public bool IsText { get; set; }

    public bool IsReal { get; set; }

    public bool IsUsed { get; set; }

    public bool IsConstant { get; set; }

    public bool Hidden { get; set; }

    public bool Service { get; set; }

    public bool External { get; set; }

    public string? Comment { get; set; }

    public string? GroupName { get; set; }

    public string? Unit { get; set; }

    public string? ListType { get; set; }

    public string? GroupType { get; set; }

    public string? ErrorState { get; set; }

    public string? ErrorString { get; set; }

    public List<string> AllowedValues { get; set; } = [];
}

public sealed class TFlexControlInspection
{
    public string? ObjectName { get; set; }

    public string? ObjectType { get; set; }

    public string? ControlType { get; set; }

    public string? Level { get; set; }

    public string? LevelVariable { get; set; }

    public string? LevelValue { get; set; }

    public string? Variable { get; set; }

    public string? VariableExpression { get; set; }

    public string? VariableValue { get; set; }

    public List<string> AllowedValues { get; set; } = [];

    public string? Caption { get; set; }

    public object? ValueOn { get; set; }

    public object? ValueOff { get; set; }
}

public sealed record TemplateComponentFile(
    string RelativePath,
    long SizeBytes,
    string Kind,
    bool IsPotentialTemplateDependency);
