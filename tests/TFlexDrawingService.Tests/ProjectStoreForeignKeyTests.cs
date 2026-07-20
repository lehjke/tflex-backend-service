using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Api.Data;
using TFlexDrawingService.Infrastructure.Configuration;

namespace TFlexDrawingService.Tests;

public sealed class ProjectStoreForeignKeyTests
{
    [Fact]
    public async Task DeletesApplyConfiguredForeignKeyActions()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        var databasePath = Path.Combine(root, "storage", "drawings.db");
        var store = new ProjectStore(Options.Create(new DrawingStorageOptions
        {
            RootPath = Path.Combine(root, "storage"),
            DatabasePath = databasePath
        }));

        await store.InitializeAsync();
        var project = await store.CreateProjectAsync("operator", "Lift", null, null);
        var configuration = await store.SaveConfigurationAsync(
            "operator",
            project.Id,
            "Configuration",
            "template-a",
            "pdf",
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("{}")!);
        Assert.NotNull(configuration);

        var specificationId = Guid.NewGuid().ToString("n");
        await InsertPricingSpecificationAsync(databasePath, specificationId, project.Id, configuration.Id);

        Assert.True(await store.DeleteConfigurationAsync(configuration.Id, "operator"));
        Assert.Null(await ReadConfigurationIdAsync(databasePath, specificationId));

        Assert.True(await store.DeleteProjectAsync(project.Id, "operator"));
        Assert.Equal(0L, await CountPricingSpecificationsAsync(databasePath, specificationId));
    }

    [Fact]
    public async Task InitializationRemovesLegacyOrphanedPricingSpecifications()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        var databasePath = Path.Combine(root, "storage", "drawings.db");
        var options = Options.Create(new DrawingStorageOptions
        {
            RootPath = Path.Combine(root, "storage"),
            DatabasePath = databasePath
        });
        var store = new ProjectStore(options);
        await store.InitializeAsync();

        var specificationId = Guid.NewGuid().ToString("n");
        await InsertPricingSpecificationAsync(
            databasePath,
            specificationId,
            "missing-project",
            "missing-configuration");

        await new ProjectStore(options).InitializeAsync();

        Assert.Equal(0L, await CountPricingSpecificationsAsync(databasePath, specificationId));
    }

    [Fact]
    public async Task SavePricingSpecification_RejectsUnknownOrCrossProjectConfiguration()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        var store = new ProjectStore(Options.Create(new DrawingStorageOptions
        {
            RootPath = Path.Combine(root, "storage"),
            DatabasePath = Path.Combine(root, "storage", "drawings.db")
        }));
        await store.InitializeAsync();

        var firstProject = await store.CreateProjectAsync("operator", "First", null, null);
        var secondProject = await store.CreateProjectAsync("operator", "Second", null, null);
        var secondConfiguration = await store.SaveConfigurationAsync(
            "operator",
            secondProject.Id,
            "Configuration",
            "template-a",
            "pdf",
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("{}")!);
        Assert.NotNull(secondConfiguration);

        var request = CreatePricingRequest(secondConfiguration.Id);
        var calculation = CreatePricingResult();

        Assert.Null(await store.SavePricingSpecificationAsync(
            "operator",
            firstProject.Id,
            secondConfiguration.Id,
            "Cross-project",
            request,
            calculation));
        Assert.Null(await store.SavePricingSpecificationAsync(
            "operator",
            firstProject.Id,
            "missing",
            "Missing",
            request,
            calculation));
        Assert.NotNull(await store.SavePricingSpecificationAsync(
            "operator",
            secondProject.Id,
            secondConfiguration.Id,
            "Valid",
            request,
            calculation));
    }

    private static PricingCalculationRequest CreatePricingRequest(string? configurationId)
    {
        return new PricingCalculationRequest(
            "SMEC",
            "LEHY-L-Pro",
            1050,
            1m,
            5,
            900,
            null,
            null,
            5,
            0,
            null,
            [],
            false,
            false,
            "RUB",
            null,
            configurationId,
            null,
            "Specification");
    }

    private static PricingCalculationResult CreatePricingResult()
    {
        return new PricingCalculationResult(
            "ready",
            "SMEC",
            "LEHY-L-Pro",
            "CNY",
            "RUB",
            12m,
            "test",
            100m,
            1200m,
            [],
            [],
            [],
            null,
            DateTimeOffset.UtcNow);
    }

    private static async Task InsertPricingSpecificationAsync(
        string databasePath,
        string specificationId,
        string projectId,
        string configurationId)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = false,
            Pooling = false
        };
        await using var connection = new SqliteConnection(connectionString.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO PricingSpecifications (
                Id, ProjectId, ProjectConfigurationId, Name, Supplier, Series, Status, TotalCny,
                TargetCurrency, TotalConverted, RequestJson, CalculationJson, CreatedAt, UpdatedAt)
            VALUES (
                $id, $projectId, $configurationId, 'Specification', 'SMEC', 'Series', 'ready', 1,
                'RUB', 1, '{}', '{}', $createdAt, $updatedAt);
            """;
        command.Parameters.AddWithValue("$id", specificationId);
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$configurationId", configurationId);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ReadConfigurationIdAsync(string databasePath, string specificationId)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ProjectConfigurationId FROM PricingSpecifications WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", specificationId);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static async Task<long> CountPricingSpecificationsAsync(string databasePath, string specificationId)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM PricingSpecifications WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", specificationId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }
}
