using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Core.Models;
using TFlexDrawingService.Infrastructure.Configuration;

namespace TFlexDrawingService.Infrastructure.Persistence;

public sealed class TemplateAnalysisStore(IOptions<DrawingStorageOptions> options)
{
    private readonly DrawingStorageOptions _options = options.Value;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_options.DatabasePath) ?? _options.RootPath);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS TemplateAnalysisJobs (
                Id TEXT PRIMARY KEY,
                Status TEXT NOT NULL,
                OwnerUserName TEXT NOT NULL,
                OriginalTemplateFileName TEXT NOT NULL,
                TemplateFilePath TEXT NOT NULL,
                ComponentsDirectoryPath TEXT NOT NULL,
                DraftJson TEXT NOT NULL DEFAULT '',
                InspectionJson TEXT NOT NULL DEFAULT '',
                WarningsJson TEXT NOT NULL DEFAULT '[]',
                ComponentManifestJson TEXT NOT NULL DEFAULT '[]',
                CreatedAt TEXT NOT NULL,
                StartedAt TEXT NULL,
                FinishedAt TEXT NULL,
                PublishedAt TEXT NULL,
                ErrorMessage TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_TemplateAnalysisJobs_Status_CreatedAt
                ON TemplateAnalysisJobs(Status, CreatedAt);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CreateAsync(
        TemplateAnalysisJob job,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO TemplateAnalysisJobs (
                Id, Status, OwnerUserName, OriginalTemplateFileName, TemplateFilePath,
                ComponentsDirectoryPath, DraftJson, InspectionJson, WarningsJson,
                ComponentManifestJson, CreatedAt, StartedAt, FinishedAt, PublishedAt, ErrorMessage)
            VALUES (
                $id, $status, $owner, $fileName, $templatePath,
                $componentsPath, $draft, $inspection, $warnings,
                $components, $createdAt, NULL, NULL, NULL, NULL);
            """;
        AddJobParameters(command, job);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> TryCreateAsync(
        TemplateAnalysisJob job,
        int maxActiveJobs,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxActiveJobs);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        await using (var countCommand = connection.CreateCommand())
        {
            countCommand.Transaction = transaction;
            countCommand.CommandText = """
                SELECT COUNT(*) FROM TemplateAnalysisJobs
                WHERE Status IN ('Pending', 'Processing');
                """;
            var active = Convert.ToInt32(
                await countCommand.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);
            if (active >= maxActiveJobs)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO TemplateAnalysisJobs (
                Id, Status, OwnerUserName, OriginalTemplateFileName, TemplateFilePath,
                ComponentsDirectoryPath, DraftJson, InspectionJson, WarningsJson,
                ComponentManifestJson, CreatedAt, StartedAt, FinishedAt, PublishedAt, ErrorMessage)
            VALUES (
                $id, $status, $owner, $fileName, $templatePath,
                $componentsPath, $draft, $inspection, $warnings,
                $components, $createdAt, NULL, NULL, NULL, NULL);
            """;
        AddJobParameters(command, job);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<TemplateAnalysisJob?> GetAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectColumns + " WHERE Id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<TemplateAnalysisJob>> ListAsync(
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectColumns + " ORDER BY CreatedAt DESC LIMIT $take;";
        command.Parameters.AddWithValue("$take", Math.Clamp(take, 1, 100));
        var result = new List<TemplateAnalysisJob>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(Map(reader));
        }

        return result;
    }

    public async Task<TemplateAnalysisJob?> TryClaimNextAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE TemplateAnalysisJobs
            SET Status = 'Processing', StartedAt = $startedAt, FinishedAt = NULL, ErrorMessage = NULL
            WHERE Id = (
                SELECT Id FROM TemplateAnalysisJobs
                WHERE Status = 'Pending'
                ORDER BY CreatedAt
                LIMIT 1
            )
            RETURNING Id, Status, OwnerUserName, OriginalTemplateFileName, TemplateFilePath,
                      ComponentsDirectoryPath, DraftJson, InspectionJson, WarningsJson,
                      ComponentManifestJson, CreatedAt, StartedAt, FinishedAt, PublishedAt, ErrorMessage;
            """;
        command.Parameters.AddWithValue("$startedAt", ToDatabaseDate(DateTimeOffset.UtcNow));
        TemplateAnalysisJob? job = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                job = Map(reader);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return job;
    }

    public Task CompleteAsync(
        string id,
        string draftJson,
        string inspectionJson,
        string warningsJson,
        string componentManifestJson,
        CancellationToken cancellationToken = default)
    {
        return ExecuteUpdateAsync(
            """
            UPDATE TemplateAnalysisJobs
            SET Status = 'Completed', DraftJson = $draft, InspectionJson = $inspection,
                WarningsJson = $warnings, ComponentManifestJson = $components,
                FinishedAt = $finishedAt, ErrorMessage = NULL
            WHERE Id = $id AND Status = 'Processing';
            """,
            command =>
            {
                command.Parameters.AddWithValue("$draft", draftJson);
                command.Parameters.AddWithValue("$inspection", inspectionJson);
                command.Parameters.AddWithValue("$warnings", warningsJson);
                command.Parameters.AddWithValue("$components", componentManifestJson);
                command.Parameters.AddWithValue("$finishedAt", ToDatabaseDate(DateTimeOffset.UtcNow));
            },
            id,
            cancellationToken);
    }

    public Task FailAsync(
        string id,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        return ExecuteUpdateAsync(
            """
            UPDATE TemplateAnalysisJobs
            SET Status = 'Failed', FinishedAt = $finishedAt, ErrorMessage = $error
            WHERE Id = $id AND Status = 'Processing';
            """,
            command =>
            {
                command.Parameters.AddWithValue("$error", errorMessage);
                command.Parameters.AddWithValue("$finishedAt", ToDatabaseDate(DateTimeOffset.UtcNow));
            },
            id,
            cancellationToken);
    }

    public Task UpdateDraftAsync(
        string id,
        string draftJson,
        CancellationToken cancellationToken = default)
    {
        return ExecuteUpdateAsync(
            """
            UPDATE TemplateAnalysisJobs SET DraftJson = $draft
            WHERE Id = $id AND Status = 'Completed';
            """,
            command => command.Parameters.AddWithValue("$draft", draftJson),
            id,
            cancellationToken);
    }

    public Task MarkPublishedAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        return ExecuteUpdateAsync(
            """
            UPDATE TemplateAnalysisJobs
            SET Status = 'Published', PublishedAt = $publishedAt
            WHERE Id = $id AND Status = 'Completed';
            """,
            command => command.Parameters.AddWithValue(
                "$publishedAt",
                ToDatabaseDate(DateTimeOffset.UtcNow)),
            id,
            cancellationToken);
    }

    public async Task<int> RecoverInterruptedAsync(
        TimeSpan olderThan,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE TemplateAnalysisJobs
            SET Status = 'Pending', StartedAt = NULL, ErrorMessage = NULL
            WHERE Status = 'Processing' AND StartedAt < $cutoff;
            """;
        command.Parameters.AddWithValue("$cutoff", ToDatabaseDate(DateTimeOffset.UtcNow - olderThan));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ExecuteUpdateAsync(
        string sql,
        Action<SqliteCommand> configure,
        string id,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id);
        configure(command);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected != 1)
        {
            throw new InvalidOperationException(
                $"Template analysis job '{id}' is not in the expected state.");
        }
    }

    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true
        }.ToString());
    }

    private static void AddJobParameters(SqliteCommand command, TemplateAnalysisJob job)
    {
        command.Parameters.AddWithValue("$id", job.Id);
        command.Parameters.AddWithValue("$status", job.Status.ToString());
        command.Parameters.AddWithValue("$owner", job.OwnerUserName);
        command.Parameters.AddWithValue("$fileName", job.OriginalTemplateFileName);
        command.Parameters.AddWithValue("$templatePath", job.TemplateFilePath);
        command.Parameters.AddWithValue("$componentsPath", job.ComponentsDirectoryPath);
        command.Parameters.AddWithValue("$draft", job.DraftJson);
        command.Parameters.AddWithValue("$inspection", job.InspectionJson);
        command.Parameters.AddWithValue("$warnings", job.WarningsJson);
        command.Parameters.AddWithValue("$components", job.ComponentManifestJson);
        command.Parameters.AddWithValue("$createdAt", ToDatabaseDate(job.CreatedAt));
    }

    private static TemplateAnalysisJob Map(SqliteDataReader reader)
    {
        return new TemplateAnalysisJob
        {
            Id = reader.GetString(0),
            Status = Enum.Parse<TemplateAnalysisStatus>(reader.GetString(1), ignoreCase: true),
            OwnerUserName = reader.GetString(2),
            OriginalTemplateFileName = reader.GetString(3),
            TemplateFilePath = reader.GetString(4),
            ComponentsDirectoryPath = reader.GetString(5),
            DraftJson = reader.GetString(6),
            InspectionJson = reader.GetString(7),
            WarningsJson = reader.GetString(8),
            ComponentManifestJson = reader.GetString(9),
            CreatedAt = ParseDate(reader.GetString(10)),
            StartedAt = ReadNullableDate(reader, 11),
            FinishedAt = ReadNullableDate(reader, 12),
            PublishedAt = ReadNullableDate(reader, 13),
            ErrorMessage = reader.IsDBNull(14) ? null : reader.GetString(14)
        };
    }

    private static DateTimeOffset? ReadNullableDate(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : ParseDate(reader.GetString(ordinal));
    }

    private static DateTimeOffset ParseDate(string value)
    {
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static string ToDatabaseDate(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private const string SelectColumns = """
        SELECT Id, Status, OwnerUserName, OriginalTemplateFileName, TemplateFilePath,
               ComponentsDirectoryPath, DraftJson, InspectionJson, WarningsJson,
               ComponentManifestJson, CreatedAt, StartedAt, FinishedAt, PublishedAt, ErrorMessage
        FROM TemplateAnalysisJobs
        """;
}
