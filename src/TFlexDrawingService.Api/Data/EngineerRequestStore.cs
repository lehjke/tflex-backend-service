using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Infrastructure.Configuration;

namespace TFlexDrawingService.Api.Data;

public sealed class EngineerRequestStore(IOptions<DrawingStorageOptions> storageOptions)
{
    private readonly DrawingStorageOptions _storageOptions = storageOptions.Value;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_storageOptions.DatabasePath) ?? _storageOptions.RootPath);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS EngineerRequests (
                Id TEXT PRIMARY KEY, ProjectId TEXT NOT NULL, Seller TEXT NOT NULL, ProjectName TEXT NOT NULL, Description TEXT NOT NULL,
                TemplateId TEXT NOT NULL, OutputFormat TEXT NOT NULL, ParametersJson TEXT NOT NULL,
                EngineerParametersJson TEXT NOT NULL, Status TEXT NOT NULL, Assignee TEXT NULL,
                LinkedJobId TEXT NULL, IssuedResultJson TEXT NULL, IssuedAt TEXT NULL,
                CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL, ConfirmationFingerprint TEXT NULL,
                FOREIGN KEY(ProjectId) REFERENCES UserProjects(Id) ON DELETE RESTRICT
            );
            CREATE INDEX IF NOT EXISTS IX_EngineerRequests_Status_UpdatedAt ON EngineerRequests(Status, UpdatedAt);
            CREATE TABLE IF NOT EXISTS EngineerRequestEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, RequestId TEXT NOT NULL, Actor TEXT NOT NULL,
                Kind TEXT NOT NULL, DetailsJson TEXT NOT NULL, CreatedAt TEXT NOT NULL,
                FOREIGN KEY(RequestId) REFERENCES EngineerRequests(Id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS EngineerRequestComments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, RequestId TEXT NOT NULL, Author TEXT NOT NULL,
                Body TEXT NOT NULL, CreatedAt TEXT NOT NULL,
                FOREIGN KEY(RequestId) REFERENCES EngineerRequests(Id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS EngineerRequestReads (
                RequestId TEXT NOT NULL, UserName TEXT NOT NULL, ReadAt TEXT NOT NULL,
                PRIMARY KEY(RequestId, UserName),
                FOREIGN KEY(RequestId) REFERENCES EngineerRequests(Id) ON DELETE CASCADE
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await using var columns = connection.CreateCommand();
        columns.CommandText = "PRAGMA table_info(EngineerRequests)";
        var hasConfirmationFingerprint = false;
        await using (var reader = await columns.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                hasConfirmationFingerprint |= string.Equals(reader.GetString(1), "ConfirmationFingerprint", StringComparison.OrdinalIgnoreCase);
        if (!hasConfirmationFingerprint)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE EngineerRequests ADD COLUMN ConfirmationFingerprint TEXT NULL";
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<EngineerRequest> CreateAsync(string projectId, string seller, string projectName, string description, string templateId, string outputFormat,
        string parametersJson, CancellationToken cancellationToken = default)
    {
        ValidateJson(parametersJson);
        var now = DateTimeOffset.UtcNow;
        var request = new EngineerRequest(Guid.NewGuid().ToString("N"), projectId, seller, projectName, description, templateId, outputFormat,
            parametersJson, "{}", EngineerRequestStatus.New, null, null, null, null, now, now);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO EngineerRequests (Id,ProjectId,Seller,ProjectName,Description,TemplateId,OutputFormat,ParametersJson,EngineerParametersJson,Status,Assignee,LinkedJobId,IssuedResultJson,IssuedAt,CreatedAt,UpdatedAt,ConfirmationFingerprint) VALUES ($id,$projectId,$seller,$project,$description,$template,$format,$parameters,'{}',$status,NULL,NULL,NULL,NULL,$now,$now,NULL);";
        command.Parameters.AddWithValue("$id", request.Id); command.Parameters.AddWithValue("$projectId", projectId); command.Parameters.AddWithValue("$seller", seller);
        command.Parameters.AddWithValue("$project", projectName); command.Parameters.AddWithValue("$description", description); command.Parameters.AddWithValue("$template", templateId);
        command.Parameters.AddWithValue("$format", outputFormat); command.Parameters.AddWithValue("$parameters", parametersJson);
        command.Parameters.AddWithValue("$status", request.Status.ToString()); command.Parameters.AddWithValue("$now", Stamp(now));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await AddEventAsync(connection, transaction, request.Id, seller, "Created", "{}", now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return request;
    }

    public async Task<EngineerRequest?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection(); await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT * FROM EngineerRequests WHERE Id=$id";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<EngineerRequest?> GetByJobIdAsync(string jobId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection(); await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT * FROM EngineerRequests WHERE LinkedJobId=$jobId";
        command.Parameters.AddWithValue("$jobId", jobId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<EngineerRequest>> ListAsync(string? status = null, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection(); await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM EngineerRequests WHERE $status IS NULL OR Status=$status ORDER BY UpdatedAt DESC";
        command.Parameters.AddWithValue("$status", status is null ? DBNull.Value : status);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<EngineerRequest>(); while (await reader.ReadAsync(cancellationToken)) rows.Add(Map(reader)); return rows;
    }

    public Task<bool> ClaimAsync(string id, string assignee, CancellationToken cancellationToken = default) =>
        MutateAsync(id, assignee, "Claimed", "Status='InProgress', Assignee=$value", "Status='New' AND Assignee IS NULL", assignee, cancellationToken);

    public async Task<bool> UpdateEngineerParametersAsync(string id, string actor, string parametersJson, CancellationToken cancellationToken = default, bool isAdmin = false)
    {
        ValidateJson(parametersJson);
        return await MutateAsync(id, actor, "ParametersUpdated", "EngineerParametersJson=$value, ConfirmationFingerprint=NULL", "Status IN ('InProgress','NeedsClarification') AND LinkedJobId IS NULL", parametersJson, cancellationToken, requireAssignee: true, isAdmin: isAdmin);
    }

    public Task<bool> TransitionAsync(string id, string actor, EngineerRequestStatus status, CancellationToken cancellationToken = default)
    {
        var allowed = status switch
        {
            EngineerRequestStatus.NeedsClarification => "Status='InProgress'",
            EngineerRequestStatus.Rejected or EngineerRequestStatus.Cancelled => "Status IN ('New','InProgress','NeedsClarification')",
            _ => "0"
        };
        return MutateAsync(id, actor, "StatusChanged", "Status=$value", allowed, status.ToString(), cancellationToken);
    }

    public Task<bool> ClarifyAsync(string id, string actor, string message, CancellationToken cancellationToken = default, bool isAdmin = false) =>
        TransitionWithCommentAsync(id, actor, message, EngineerRequestStatus.NeedsClarification, "Status='InProgress'", cancellationToken, isAdmin);

    public Task<bool> RejectAsync(string id, string actor, string message, CancellationToken cancellationToken = default, bool isAdmin = false) =>
        TransitionWithCommentAsync(id, actor, message, EngineerRequestStatus.Rejected,
            "Status IN ('New','InProgress','NeedsClarification')", cancellationToken, isAdmin);

    private async Task<bool> TransitionWithCommentAsync(string id, string actor, string message,
        EngineerRequestStatus status, string condition, CancellationToken cancellationToken, bool isAdmin)
    {
        await using var connection = CreateConnection(); await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"UPDATE EngineerRequests SET Status=$status, UpdatedAt=$now WHERE Id=$id AND {condition} AND ($isAdmin=1 OR Assignee=$actor)";
        command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$now", Stamp(now));
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$actor", actor); command.Parameters.AddWithValue("$isAdmin", isAdmin ? 1 : 0);
        var changed = await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        if (changed)
        {
            await using var comment = connection.CreateCommand(); comment.Transaction = transaction;
            comment.CommandText = "INSERT INTO EngineerRequestComments(RequestId,Author,Body,CreatedAt) VALUES($id,$actor,$message,$now)";
            comment.Parameters.AddWithValue("$id", id); comment.Parameters.AddWithValue("$actor", actor); comment.Parameters.AddWithValue("$message", message); comment.Parameters.AddWithValue("$now", Stamp(now));
            await comment.ExecuteNonQueryAsync(cancellationToken);
            await AddEventAsync(connection, transaction, id, actor, "StatusChanged", JsonSerializer.Serialize(new { status = status.ToString(), message }), now, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken); return changed;
    }

    public Task<bool> ReserveJobAsync(string id, string actor, string jobId, string? confirmationFingerprint = null, CancellationToken cancellationToken = default, bool isAdmin = false) =>
        MutateAsync(id, actor, "JobReserved", "LinkedJobId=$value, ConfirmationFingerprint=$fingerprint", "Status='InProgress' AND LinkedJobId IS NULL AND IssuedResultJson IS NULL", jobId, cancellationToken, confirmationFingerprint: confirmationFingerprint, requireAssignee: true, isAdmin: isAdmin);

    public Task<bool> ClearLinkedJobAsync(string id, string actor, string jobId, CancellationToken cancellationToken = default, bool isAdmin = false) =>
        MutateAsync(id, actor, "JobReservationCleared", "LinkedJobId=NULL, ConfirmationFingerprint=NULL", "Status IN ('InProgress','NeedsClarification') AND LinkedJobId=$job AND IssuedResultJson IS NULL", "", cancellationToken, jobId, requireAssignee: true, isAdmin: isAdmin);

    public Task<bool> ResumeAsync(string id, string actor, CancellationToken cancellationToken = default) =>
        MutateAsync(id, actor, "Resumed", "Status='InProgress'", "Status='NeedsClarification'", "InProgress", cancellationToken);

    public Task<bool> ReassignAsync(string id, string actor, string assignee, CancellationToken cancellationToken = default) =>
        MutateAsync(id, actor, "Reassigned", "Assignee=$value", "Status='InProgress'", assignee, cancellationToken);

    public async Task<bool> PublishResultAsync(string id, string actor, string linkedJobId, string resultJson, CancellationToken cancellationToken = default, bool isAdmin = false)
    {
        ValidateJson(resultJson);
        return await MutateAsync(id, actor, "ResultIssued", "Status='Ready', IssuedResultJson=$value, IssuedAt=$now",
            "Status='InProgress' AND LinkedJobId=$job AND IssuedResultJson IS NULL", resultJson, cancellationToken, linkedJobId, requireAssignee: true, isAdmin: isAdmin);
    }

    public async Task<bool> AddCommentAsync(string id, string author, string body, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection(); await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO EngineerRequestComments(RequestId,Author,Body,CreatedAt) SELECT $id,$author,$body,$now WHERE EXISTS(SELECT 1 FROM EngineerRequests WHERE Id=$id AND Status IN ('New','InProgress','NeedsClarification'))";
        command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$author", author); command.Parameters.AddWithValue("$body", body); command.Parameters.AddWithValue("$now", Stamp(now));
        var changed = await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        if (changed)
        {
            await using var update = connection.CreateCommand(); update.Transaction = transaction;
            update.CommandText = "UPDATE EngineerRequests SET UpdatedAt=$now WHERE Id=$id";
            update.Parameters.AddWithValue("$id", id); update.Parameters.AddWithValue("$now", Stamp(now));
            await update.ExecuteNonQueryAsync(cancellationToken);
            await AddEventAsync(connection, transaction, id, author, "CommentAdded", JsonSerializer.Serialize(new { body }), now, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken); return changed;
    }

    public async Task MarkReadAsync(string id, string userName, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection(); await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO EngineerRequestReads VALUES($id,$user,$now) ON CONFLICT(RequestId,UserName) DO UPDATE SET ReadAt=excluded.ReadAt";
        command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$user", userName); command.Parameters.AddWithValue("$now", Stamp(DateTimeOffset.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> IsReadAsync(string id, string userName, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection(); await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT EXISTS(SELECT 1 FROM EngineerRequestReads rd JOIN EngineerRequests req ON req.Id=rd.RequestId WHERE rd.RequestId=$id AND rd.UserName=$user AND rd.ReadAt>=req.UpdatedAt)";
        command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$user", userName);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 0;
    }

    public Task<IReadOnlyList<EngineerRequestEvent>> GetEventsAsync(string id, CancellationToken cancellationToken = default) => ReadHistoryAsync<EngineerRequestEvent>(id, "EngineerRequestEvents", cancellationToken);
    public Task<IReadOnlyList<EngineerRequestComment>> GetCommentsAsync(string id, CancellationToken cancellationToken = default) => ReadHistoryAsync<EngineerRequestComment>(id, "EngineerRequestComments", cancellationToken);

    private async Task<IReadOnlyList<T>> ReadHistoryAsync<T>(string id, string table, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection(); await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(); command.CommandText = $"SELECT * FROM {table} WHERE RequestId=$id ORDER BY Id"; command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<T>();
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(typeof(T) == typeof(EngineerRequestEvent)
                ? (T)(object)new EngineerRequestEvent(reader.GetInt64(0), id, reader.GetString(2), reader.GetString(3), reader.GetString(4), Parse(reader.GetString(5)))
                : (T)(object)new EngineerRequestComment(reader.GetInt64(0), id, reader.GetString(2), reader.GetString(3), Parse(reader.GetString(4))));
        return rows;
    }

    private async Task<bool> MutateAsync(string id, string actor, string kind, string assignments, string condition, string value,
        CancellationToken cancellationToken, string? linkedJobId = null, string? confirmationFingerprint = null, bool requireAssignee = false, bool isAdmin = false)
    {
        await using var connection = CreateConnection(); await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"UPDATE EngineerRequests SET {assignments}, UpdatedAt=$now WHERE Id=$id AND {condition}{(requireAssignee ? " AND ($isAdmin=1 OR Assignee=$actor)" : "")}";
        command.Parameters.AddWithValue("$value", value); command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$now", Stamp(now));
        if (requireAssignee) { command.Parameters.AddWithValue("$actor", actor); command.Parameters.AddWithValue("$isAdmin", isAdmin ? 1 : 0); }
        if (command.CommandText.Contains("$job", StringComparison.Ordinal)) command.Parameters.AddWithValue("$job", linkedJobId!);
        if (command.CommandText.Contains("$fingerprint", StringComparison.Ordinal)) command.Parameters.AddWithValue("$fingerprint", (object?)confirmationFingerprint ?? DBNull.Value);
        var changed = await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        if (changed) await AddEventAsync(connection, transaction, id, actor, kind, JsonSerializer.Serialize(new { value, linkedJobId }), now, cancellationToken);
        await transaction.CommitAsync(cancellationToken); return changed;
    }

    private static async Task AddEventAsync(SqliteConnection connection, SqliteTransaction transaction, string id, string actor, string kind, string details, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO EngineerRequestEvents(RequestId,Actor,Kind,DetailsJson,CreatedAt) VALUES($id,$actor,$kind,$details,$now)";
        command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$actor", actor); command.Parameters.AddWithValue("$kind", kind); command.Parameters.AddWithValue("$details", details); command.Parameters.AddWithValue("$now", Stamp(now));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private SqliteConnection CreateConnection() => new(new SqliteConnectionStringBuilder { DataSource = _storageOptions.DatabasePath, ForeignKeys = true }.ToString());
    private static void ValidateJson(string json) { using var _ = JsonDocument.Parse(json); }
    private static string Stamp(DateTimeOffset value) => value.ToUniversalTime().ToString("O");
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    private static EngineerRequest Map(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7), r.GetString(8), Enum.Parse<EngineerRequestStatus>(r.GetString(9)), r.IsDBNull(10) ? null : r.GetString(10), r.IsDBNull(11) ? null : r.GetString(11), r.IsDBNull(12) ? null : r.GetString(12), r.IsDBNull(13) ? null : Parse(r.GetString(13)), Parse(r.GetString(14)), Parse(r.GetString(15)), r.IsDBNull(16) ? null : r.GetString(16));
}

public enum EngineerRequestStatus { New, InProgress, NeedsClarification, Ready, Rejected, Cancelled }
public sealed record EngineerRequest(string Id, string ProjectId, string Seller, string ProjectName, string Description, string TemplateId, string OutputFormat, string ParametersJson, string EngineerParametersJson, EngineerRequestStatus Status, string? Assignee, string? LinkedJobId, string? IssuedResultJson, DateTimeOffset? IssuedAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? ConfirmationFingerprint = null);
public sealed record EngineerRequestEvent(long Id, string RequestId, string Actor, string Kind, string DetailsJson, DateTimeOffset CreatedAt);
public sealed record EngineerRequestComment(long Id, string RequestId, string Author, string Body, DateTimeOffset CreatedAt);
