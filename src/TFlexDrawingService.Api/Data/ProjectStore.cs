using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Infrastructure.Configuration;

namespace TFlexDrawingService.Api.Data;

public sealed class ProjectStore(IOptions<DrawingStorageOptions> storageOptions)
{
    private readonly DrawingStorageOptions _storageOptions = storageOptions.Value;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_storageOptions.DatabasePath) ?? _storageOptions.RootPath);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS UserProjects (
                Id TEXT PRIMARY KEY,
                OwnerUserName TEXT NOT NULL,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_UserProjects_OwnerUserName_UpdatedAt
                ON UserProjects(OwnerUserName, UpdatedAt);

            CREATE TABLE IF NOT EXISTS ProjectConfigurations (
                Id TEXT PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                Name TEXT NOT NULL,
                TemplateId TEXT NOT NULL,
                OutputFormat TEXT NOT NULL,
                ParametersJson TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY(ProjectId) REFERENCES UserProjects(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_ProjectConfigurations_ProjectId_UpdatedAt
                ON ProjectConfigurations(ProjectId, UpdatedAt);

            CREATE TABLE IF NOT EXISTS PricingSpecifications (
                Id TEXT PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                ProjectConfigurationId TEXT NULL,
                Name TEXT NOT NULL,
                Supplier TEXT NOT NULL,
                Series TEXT NOT NULL,
                Status TEXT NOT NULL,
                TotalCny REAL NOT NULL,
                TargetCurrency TEXT NOT NULL,
                TotalConverted REAL NOT NULL,
                RequestJson TEXT NOT NULL,
                CalculationJson TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY(ProjectId) REFERENCES UserProjects(Id) ON DELETE CASCADE,
                FOREIGN KEY(ProjectConfigurationId) REFERENCES ProjectConfigurations(Id) ON DELETE SET NULL
            );

            CREATE INDEX IF NOT EXISTS IX_PricingSpecifications_ProjectId_UpdatedAt
                ON PricingSpecifications(ProjectId, UpdatedAt);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserProject>> ListProjectsAsync(
        string ownerUserName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, OwnerUserName, Name, Description, CreatedAt, UpdatedAt
            FROM UserProjects
            WHERE OwnerUserName = $ownerUserName
            ORDER BY UpdatedAt DESC;
            """;
        command.Parameters.AddWithValue("$ownerUserName", ownerUserName);

        var projects = new List<UserProject>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            projects.Add(MapProject(reader));
        }

        return projects;
    }

    public async Task<UserProject?> GetProjectAsync(
        string projectId,
        string ownerUserName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, OwnerUserName, Name, Description, CreatedAt, UpdatedAt
            FROM UserProjects
            WHERE Id = $id AND OwnerUserName = $ownerUserName;
            """;
        command.Parameters.AddWithValue("$id", projectId);
        command.Parameters.AddWithValue("$ownerUserName", ownerUserName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapProject(reader) : null;
    }

    public async Task<UserProject> CreateProjectAsync(
        string ownerUserName,
        string name,
        string? description,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var project = new UserProject(
            Guid.NewGuid().ToString("n"),
            ownerUserName,
            NormalizeName(name, "Новый проект"),
            description?.Trim() ?? string.Empty,
            now,
            now);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO UserProjects (Id, OwnerUserName, Name, Description, CreatedAt, UpdatedAt)
            VALUES ($id, $ownerUserName, $name, $description, $createdAt, $updatedAt);
            """;
        command.Parameters.AddWithValue("$id", project.Id);
        command.Parameters.AddWithValue("$ownerUserName", project.OwnerUserName);
        command.Parameters.AddWithValue("$name", project.Name);
        command.Parameters.AddWithValue("$description", project.Description);
        command.Parameters.AddWithValue("$createdAt", FormatDate(project.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", FormatDate(project.UpdatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);

        return project;
    }

    public async Task<bool> DeleteProjectAsync(
        string projectId,
        string ownerUserName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var configurationsCommand = connection.CreateCommand())
        {
            configurationsCommand.Transaction = transaction;
            configurationsCommand.CommandText = """
                DELETE FROM ProjectConfigurations
                WHERE ProjectId IN (
                    SELECT Id FROM UserProjects WHERE Id = $projectId AND OwnerUserName = $ownerUserName
                );
                """;
            configurationsCommand.Parameters.AddWithValue("$projectId", projectId);
            configurationsCommand.Parameters.AddWithValue("$ownerUserName", ownerUserName);
            await configurationsCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var projectCommand = connection.CreateCommand())
        {
            projectCommand.Transaction = transaction;
            projectCommand.CommandText = """
                DELETE FROM UserProjects
                WHERE Id = $projectId AND OwnerUserName = $ownerUserName;
                """;
            projectCommand.Parameters.AddWithValue("$projectId", projectId);
            projectCommand.Parameters.AddWithValue("$ownerUserName", ownerUserName);
            var rows = await projectCommand.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return rows > 0;
        }
    }

    public async Task<IReadOnlyList<ProjectConfiguration>> ListConfigurationsAsync(
        string projectId,
        string ownerUserName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.Id, c.ProjectId, c.Name, c.TemplateId, c.OutputFormat, c.ParametersJson, c.CreatedAt, c.UpdatedAt
            FROM ProjectConfigurations c
            INNER JOIN UserProjects p ON p.Id = c.ProjectId
            WHERE c.ProjectId = $projectId AND p.OwnerUserName = $ownerUserName
            ORDER BY c.UpdatedAt DESC;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$ownerUserName", ownerUserName);

        var configurations = new List<ProjectConfiguration>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            configurations.Add(MapConfiguration(reader));
        }

        return configurations;
    }

    public async Task<ProjectConfiguration?> GetConfigurationAsync(
        string configurationId,
        string ownerUserName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.Id, c.ProjectId, c.Name, c.TemplateId, c.OutputFormat, c.ParametersJson, c.CreatedAt, c.UpdatedAt
            FROM ProjectConfigurations c
            INNER JOIN UserProjects p ON p.Id = c.ProjectId
            WHERE c.Id = $configurationId AND p.OwnerUserName = $ownerUserName;
            """;
        command.Parameters.AddWithValue("$configurationId", configurationId);
        command.Parameters.AddWithValue("$ownerUserName", ownerUserName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapConfiguration(reader) : null;
    }

    public async Task<ProjectConfiguration?> SaveConfigurationAsync(
        string ownerUserName,
        string projectId,
        string name,
        string templateId,
        string outputFormat,
        IReadOnlyDictionary<string, JsonElement> parameters,
        CancellationToken cancellationToken = default)
    {
        if (await GetProjectAsync(projectId, ownerUserName, cancellationToken) is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var configuration = new ProjectConfiguration(
            Guid.NewGuid().ToString("n"),
            projectId,
            NormalizeName(name, "Конфигурация"),
            templateId,
            outputFormat,
            JsonSerializer.Serialize(parameters, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            now,
            now);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ProjectConfigurations (
                Id, ProjectId, Name, TemplateId, OutputFormat, ParametersJson, CreatedAt, UpdatedAt
            )
            VALUES (
                $id, $projectId, $name, $templateId, $outputFormat, $parametersJson, $createdAt, $updatedAt
            );

            UPDATE UserProjects
            SET UpdatedAt = $updatedAt
            WHERE Id = $projectId;
            """;
        command.Parameters.AddWithValue("$id", configuration.Id);
        command.Parameters.AddWithValue("$projectId", configuration.ProjectId);
        command.Parameters.AddWithValue("$name", configuration.Name);
        command.Parameters.AddWithValue("$templateId", configuration.TemplateId);
        command.Parameters.AddWithValue("$outputFormat", configuration.OutputFormat);
        command.Parameters.AddWithValue("$parametersJson", configuration.ParametersJson);
        command.Parameters.AddWithValue("$createdAt", FormatDate(configuration.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", FormatDate(configuration.UpdatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);

        return configuration;
    }

    public async Task<ProjectConfiguration?> UpdateConfigurationAsync(
        string ownerUserName,
        string configurationId,
        string name,
        string templateId,
        string outputFormat,
        IReadOnlyDictionary<string, JsonElement> parameters,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetConfigurationAsync(configurationId, ownerUserName, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var configuration = existing with
        {
            Name = NormalizeName(name, "Конфигурация"),
            TemplateId = templateId,
            OutputFormat = outputFormat,
            ParametersJson = JsonSerializer.Serialize(parameters, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            UpdatedAt = now
        };

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ProjectConfigurations
            SET Name = $name,
                TemplateId = $templateId,
                OutputFormat = $outputFormat,
                ParametersJson = $parametersJson,
                UpdatedAt = $updatedAt
            WHERE Id = $configurationId
              AND ProjectId IN (
                SELECT Id FROM UserProjects WHERE OwnerUserName = $ownerUserName
              );

            UPDATE UserProjects
            SET UpdatedAt = $updatedAt
            WHERE Id = $projectId;
            """;
        command.Parameters.AddWithValue("$configurationId", configuration.Id);
        command.Parameters.AddWithValue("$ownerUserName", ownerUserName);
        command.Parameters.AddWithValue("$projectId", configuration.ProjectId);
        command.Parameters.AddWithValue("$name", configuration.Name);
        command.Parameters.AddWithValue("$templateId", configuration.TemplateId);
        command.Parameters.AddWithValue("$outputFormat", configuration.OutputFormat);
        command.Parameters.AddWithValue("$parametersJson", configuration.ParametersJson);
        command.Parameters.AddWithValue("$updatedAt", FormatDate(configuration.UpdatedAt));

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0
            ? configuration
            : null;
    }

    public async Task<bool> DeleteConfigurationAsync(
        string configurationId,
        string ownerUserName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM ProjectConfigurations
            WHERE Id = $configurationId
              AND ProjectId IN (
                SELECT Id FROM UserProjects WHERE OwnerUserName = $ownerUserName
              );
            """;
        command.Parameters.AddWithValue("$configurationId", configurationId);
        command.Parameters.AddWithValue("$ownerUserName", ownerUserName);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<IReadOnlyList<PricingSpecification>> ListPricingSpecificationsAsync(
        string projectId,
        string ownerUserName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.Id, s.ProjectId, s.ProjectConfigurationId, s.Name, s.Supplier, s.Series, s.Status,
                   s.TotalCny, s.TargetCurrency, s.TotalConverted, s.RequestJson, s.CalculationJson,
                   s.CreatedAt, s.UpdatedAt
            FROM PricingSpecifications s
            INNER JOIN UserProjects p ON p.Id = s.ProjectId
            WHERE s.ProjectId = $projectId AND p.OwnerUserName = $ownerUserName
            ORDER BY s.UpdatedAt DESC;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$ownerUserName", ownerUserName);

        var specifications = new List<PricingSpecification>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            specifications.Add(MapPricingSpecification(reader));
        }

        return specifications;
    }

    public async Task<PricingSpecification?> GetPricingSpecificationAsync(
        string specificationId,
        string ownerUserName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.Id, s.ProjectId, s.ProjectConfigurationId, s.Name, s.Supplier, s.Series, s.Status,
                   s.TotalCny, s.TargetCurrency, s.TotalConverted, s.RequestJson, s.CalculationJson,
                   s.CreatedAt, s.UpdatedAt
            FROM PricingSpecifications s
            INNER JOIN UserProjects p ON p.Id = s.ProjectId
            WHERE s.Id = $specificationId AND p.OwnerUserName = $ownerUserName;
            """;
        command.Parameters.AddWithValue("$specificationId", specificationId);
        command.Parameters.AddWithValue("$ownerUserName", ownerUserName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapPricingSpecification(reader) : null;
    }

    public async Task<PricingSpecification?> SavePricingSpecificationAsync(
        string ownerUserName,
        string projectId,
        string? projectConfigurationId,
        string name,
        PricingCalculationRequest request,
        PricingCalculationResult calculation,
        CancellationToken cancellationToken = default)
    {
        if (await GetProjectAsync(projectId, ownerUserName, cancellationToken) is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var specification = new PricingSpecification(
            Guid.NewGuid().ToString("n"),
            projectId,
            string.IsNullOrWhiteSpace(projectConfigurationId) ? null : projectConfigurationId,
            NormalizeName(name, "Спецификация"),
            calculation.Supplier,
            calculation.Series,
            calculation.Status,
            calculation.TotalCny,
            calculation.TargetCurrency,
            calculation.TotalConverted,
            JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            JsonSerializer.Serialize(calculation, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            now,
            now);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO PricingSpecifications (
                Id, ProjectId, ProjectConfigurationId, Name, Supplier, Series, Status, TotalCny,
                TargetCurrency, TotalConverted, RequestJson, CalculationJson, CreatedAt, UpdatedAt
            )
            VALUES (
                $id, $projectId, $projectConfigurationId, $name, $supplier, $series, $status, $totalCny,
                $targetCurrency, $totalConverted, $requestJson, $calculationJson, $createdAt, $updatedAt
            );

            UPDATE UserProjects
            SET UpdatedAt = $updatedAt
            WHERE Id = $projectId;
            """;
        command.Parameters.AddWithValue("$id", specification.Id);
        command.Parameters.AddWithValue("$projectId", specification.ProjectId);
        command.Parameters.AddWithValue("$projectConfigurationId", (object?)specification.ProjectConfigurationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$name", specification.Name);
        command.Parameters.AddWithValue("$supplier", specification.Supplier);
        command.Parameters.AddWithValue("$series", specification.Series);
        command.Parameters.AddWithValue("$status", specification.Status);
        command.Parameters.AddWithValue("$totalCny", specification.TotalCny);
        command.Parameters.AddWithValue("$targetCurrency", specification.TargetCurrency);
        command.Parameters.AddWithValue("$totalConverted", specification.TotalConverted);
        command.Parameters.AddWithValue("$requestJson", specification.RequestJson);
        command.Parameters.AddWithValue("$calculationJson", specification.CalculationJson);
        command.Parameters.AddWithValue("$createdAt", FormatDate(specification.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", FormatDate(specification.UpdatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);

        return specification;
    }

    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection($"Data Source={_storageOptions.DatabasePath}");
    }

    private static UserProject MapProject(SqliteDataReader reader)
    {
        return new UserProject(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            ParseDate(reader.GetString(4)),
            ParseDate(reader.GetString(5)));
    }

    private static ProjectConfiguration MapConfiguration(SqliteDataReader reader)
    {
        return new ProjectConfiguration(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            ParseDate(reader.GetString(6)),
            ParseDate(reader.GetString(7)));
    }

    private static PricingSpecification MapPricingSpecification(SqliteDataReader reader)
    {
        return new PricingSpecification(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            Convert.ToDecimal(reader.GetDouble(7), CultureInfo.InvariantCulture),
            reader.GetString(8),
            Convert.ToDecimal(reader.GetDouble(9), CultureInfo.InvariantCulture),
            reader.GetString(10),
            reader.GetString(11),
            ParseDate(reader.GetString(12)),
            ParseDate(reader.GetString(13)));
    }

    private static string NormalizeName(string name, string fallback)
    {
        return string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
    }

    private static string FormatDate(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ParseDate(string value)
    {
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}

public sealed record UserProject(
    string Id,
    string OwnerUserName,
    string Name,
    string Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ProjectConfiguration(
    string Id,
    string ProjectId,
    string Name,
    string TemplateId,
    string OutputFormat,
    string ParametersJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PricingSpecification(
    string Id,
    string ProjectId,
    string? ProjectConfigurationId,
    string Name,
    string Supplier,
    string Series,
    string Status,
    decimal TotalCny,
    string TargetCurrency,
    decimal TotalConverted,
    string RequestJson,
    string CalculationJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
