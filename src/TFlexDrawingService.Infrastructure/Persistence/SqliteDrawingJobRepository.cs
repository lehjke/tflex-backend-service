using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Core.Abstractions;
using TFlexDrawingService.Core.Models;
using TFlexDrawingService.Infrastructure.Configuration;

namespace TFlexDrawingService.Infrastructure.Persistence;

public sealed class SqliteDrawingJobRepository(
    IOptions<DrawingStorageOptions> options,
    ILogger<SqliteDrawingJobRepository> logger) : IDrawingJobRepository
{
    private readonly DrawingStorageOptions _options = options.Value;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_options.DatabasePath) ?? _options.RootPath);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;

            CREATE TABLE IF NOT EXISTS DrawingJobs (
                Id TEXT PRIMARY KEY,
                TemplateId TEXT NOT NULL,
                Status TEXT NOT NULL,
                InputParametersJson TEXT NOT NULL,
                OutputFormat TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                StartedAt TEXT NULL,
                FinishedAt TEXT NULL,
                ErrorMessage TEXT NULL,
                WorkingDirectory TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_DrawingJobs_Status_CreatedAt
                ON DrawingJobs(Status, CreatedAt);

            CREATE TABLE IF NOT EXISTS GeneratedFiles (
                Id TEXT PRIMARY KEY,
                JobId TEXT NOT NULL,
                FileName TEXT NOT NULL,
                Format TEXT NOT NULL,
                Path TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                SizeBytes INTEGER NOT NULL,
                FOREIGN KEY(JobId) REFERENCES DrawingJobs(Id)
            );
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
        logger.LogInformation("SQLite storage initialized at {DatabasePath}", _options.DatabasePath);
    }

    public async Task CreateAsync(DrawingJob job, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DrawingJobs (
                Id, TemplateId, Status, InputParametersJson, OutputFormat, CreatedAt,
                StartedAt, FinishedAt, ErrorMessage, WorkingDirectory
            )
            VALUES (
                $id, $templateId, $status, $inputParametersJson, $outputFormat, $createdAt,
                $startedAt, $finishedAt, $errorMessage, $workingDirectory
            );
            """;
        AddJobParameters(command, job);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<DrawingJob?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var job = await GetJobCoreAsync(connection, id, cancellationToken);
        if (job is not null)
        {
            job.ResultFiles = [.. await LoadFilesAsync(connection, job.Id, cancellationToken)];
        }

        return job;
    }

    public async Task<IReadOnlyList<DrawingJob>> ListAsync(int take = 50, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, TemplateId, Status, InputParametersJson, OutputFormat, CreatedAt,
                   StartedAt, FinishedAt, ErrorMessage, WorkingDirectory
            FROM DrawingJobs
            ORDER BY CreatedAt DESC
            LIMIT $take;
            """;
        command.Parameters.AddWithValue("$take", Math.Clamp(take, 1, 200));

        var jobs = new List<DrawingJob>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(MapJob(reader));
        }

        foreach (var job in jobs)
        {
            job.ResultFiles = [.. await LoadFilesAsync(connection, job.Id, cancellationToken)];
        }

        return jobs;
    }

    public async Task<DrawingJob?> TryClaimNextPendingAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE DrawingJobs
            SET Status = $running,
                StartedAt = $startedAt
            WHERE Id = (
                SELECT Id
                FROM DrawingJobs
                WHERE Status = $pending
                ORDER BY CreatedAt
                LIMIT 1
            )
            RETURNING Id, TemplateId, Status, InputParametersJson, OutputFormat, CreatedAt,
                      StartedAt, FinishedAt, ErrorMessage, WorkingDirectory;
            """;
        command.Parameters.AddWithValue("$running", DrawingJobStatus.Running.ToString());
        command.Parameters.AddWithValue("$startedAt", FormatDate(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$pending", DrawingJobStatus.Pending.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapJob(reader) : null;
    }

    public async Task UpdateWorkingDirectoryAsync(
        string id,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(
            """
            UPDATE DrawingJobs
            SET WorkingDirectory = $workingDirectory
            WHERE Id = $id;
            """,
            command =>
            {
                command.Parameters.AddWithValue("$id", id);
                command.Parameters.AddWithValue("$workingDirectory", workingDirectory);
            },
            cancellationToken);
    }

    public async Task AddGeneratedFileAsync(GeneratedFile file, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(
            """
            INSERT INTO GeneratedFiles (Id, JobId, FileName, Format, Path, CreatedAt, SizeBytes)
            VALUES ($id, $jobId, $fileName, $format, $path, $createdAt, $sizeBytes);
            """,
            command =>
            {
                command.Parameters.AddWithValue("$id", file.Id);
                command.Parameters.AddWithValue("$jobId", file.JobId);
                command.Parameters.AddWithValue("$fileName", file.FileName);
                command.Parameters.AddWithValue("$format", file.Format);
                command.Parameters.AddWithValue("$path", file.Path);
                command.Parameters.AddWithValue("$createdAt", FormatDate(file.CreatedAt));
                command.Parameters.AddWithValue("$sizeBytes", file.SizeBytes);
            },
            cancellationToken);
    }

    public async Task<GeneratedFile?> GetGeneratedFileAsync(
        string jobId,
        string fileId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, JobId, FileName, Format, Path, CreatedAt, SizeBytes
            FROM GeneratedFiles
            WHERE JobId = $jobId AND Id = $fileId;
            """;
        command.Parameters.AddWithValue("$jobId", jobId);
        command.Parameters.AddWithValue("$fileId", fileId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapFile(reader) : null;
    }

    public async Task MarkCompletedAsync(
        string id,
        DateTimeOffset finishedAt,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(
            """
            UPDATE DrawingJobs
            SET Status = $status,
                FinishedAt = $finishedAt,
                ErrorMessage = NULL
            WHERE Id = $id;
            """,
            command =>
            {
                command.Parameters.AddWithValue("$id", id);
                command.Parameters.AddWithValue("$status", DrawingJobStatus.Completed.ToString());
                command.Parameters.AddWithValue("$finishedAt", FormatDate(finishedAt));
            },
            cancellationToken);
    }

    public async Task MarkFailedAsync(
        string id,
        string errorMessage,
        DateTimeOffset finishedAt,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(
            """
            UPDATE DrawingJobs
            SET Status = $status,
                FinishedAt = $finishedAt,
                ErrorMessage = $errorMessage
            WHERE Id = $id;
            """,
            command =>
            {
                command.Parameters.AddWithValue("$id", id);
                command.Parameters.AddWithValue("$status", DrawingJobStatus.Failed.ToString());
                command.Parameters.AddWithValue("$finishedAt", FormatDate(finishedAt));
                command.Parameters.AddWithValue("$errorMessage", errorMessage);
            },
            cancellationToken);
    }

    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection($"Data Source={_options.DatabasePath}");
    }

    private async Task<DrawingJob?> GetJobCoreAsync(
        SqliteConnection connection,
        string id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, TemplateId, Status, InputParametersJson, OutputFormat, CreatedAt,
                   StartedAt, FinishedAt, ErrorMessage, WorkingDirectory
            FROM DrawingJobs
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapJob(reader) : null;
    }

    private async Task<IReadOnlyList<GeneratedFile>> LoadFilesAsync(
        SqliteConnection connection,
        string jobId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, JobId, FileName, Format, Path, CreatedAt, SizeBytes
            FROM GeneratedFiles
            WHERE JobId = $jobId
            ORDER BY CreatedAt;
            """;
        command.Parameters.AddWithValue("$jobId", jobId);

        var files = new List<GeneratedFile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            files.Add(MapFile(reader));
        }

        return files;
    }

    private async Task ExecuteAsync(
        string commandText,
        Action<SqliteCommand> configure,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        configure(command);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddJobParameters(SqliteCommand command, DrawingJob job)
    {
        command.Parameters.AddWithValue("$id", job.Id);
        command.Parameters.AddWithValue("$templateId", job.TemplateId);
        command.Parameters.AddWithValue("$status", job.Status.ToString());
        command.Parameters.AddWithValue("$inputParametersJson", job.InputParametersJson);
        command.Parameters.AddWithValue("$outputFormat", job.OutputFormat);
        command.Parameters.AddWithValue("$createdAt", FormatDate(job.CreatedAt));
        command.Parameters.AddWithValue("$startedAt", ToDbValue(job.StartedAt));
        command.Parameters.AddWithValue("$finishedAt", ToDbValue(job.FinishedAt));
        command.Parameters.AddWithValue("$errorMessage", ToDbValue(job.ErrorMessage));
        command.Parameters.AddWithValue("$workingDirectory", ToDbValue(job.WorkingDirectory));
    }

    private static DrawingJob MapJob(SqliteDataReader reader)
    {
        return new DrawingJob
        {
            Id = reader.GetString(0),
            TemplateId = reader.GetString(1),
            Status = Enum.Parse<DrawingJobStatus>(reader.GetString(2)),
            InputParametersJson = reader.GetString(3),
            OutputFormat = reader.GetString(4),
            CreatedAt = ParseDate(reader.GetString(5)),
            StartedAt = ReadNullableDate(reader, 6),
            FinishedAt = ReadNullableDate(reader, 7),
            ErrorMessage = reader.IsDBNull(8) ? null : reader.GetString(8),
            WorkingDirectory = reader.IsDBNull(9) ? null : reader.GetString(9)
        };
    }

    private static GeneratedFile MapFile(SqliteDataReader reader)
    {
        return new GeneratedFile
        {
            Id = reader.GetString(0),
            JobId = reader.GetString(1),
            FileName = reader.GetString(2),
            Format = reader.GetString(3),
            Path = reader.GetString(4),
            CreatedAt = ParseDate(reader.GetString(5)),
            SizeBytes = reader.GetInt64(6)
        };
    }

    private static DateTimeOffset? ReadNullableDate(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : ParseDate(reader.GetString(ordinal));
    }

    private static string FormatDate(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ParseDate(string value)
    {
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static object ToDbValue(object? value)
    {
        return value ?? DBNull.Value;
    }
}
