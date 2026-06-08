using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Infrastructure.Configuration;

namespace TFlexDrawingService.Api.Data;

public sealed class TemplateAccessStore(IOptions<DrawingStorageOptions> storageOptions)
{
    private readonly DrawingStorageOptions _storageOptions = storageOptions.Value;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_storageOptions.DatabasePath) ?? _storageOptions.RootPath);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS TemplateAccess (
                TemplateId TEXT PRIMARY KEY COLLATE NOCASE,
                Enabled INTEGER NOT NULL,
                UpdatedAt TEXT NOT NULL,
                UpdatedByUserName TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, bool>> GetStatesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TemplateId, Enabled FROM TemplateAccess;";

        var states = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            states[reader.GetString(0)] = reader.GetInt32(1) == 1;
        }

        return states;
    }

    public async Task<bool> IsEnabledAsync(
        string templateId,
        CancellationToken cancellationToken = default)
    {
        var states = await GetStatesAsync(cancellationToken);
        return !states.TryGetValue(templateId, out var enabled) || enabled;
    }

    public async Task SetEnabledAsync(
        string templateId,
        bool enabled,
        string updatedByUserName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO TemplateAccess (TemplateId, Enabled, UpdatedAt, UpdatedByUserName)
            VALUES ($templateId, $enabled, $updatedAt, $updatedByUserName)
            ON CONFLICT(TemplateId) DO UPDATE SET
                Enabled = excluded.Enabled,
                UpdatedAt = excluded.UpdatedAt,
                UpdatedByUserName = excluded.UpdatedByUserName;
            """;
        command.Parameters.AddWithValue("$templateId", templateId);
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updatedByUserName", updatedByUserName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection($"Data Source={_storageOptions.DatabasePath}");
    }
}
