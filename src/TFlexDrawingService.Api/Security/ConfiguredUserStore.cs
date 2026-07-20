using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Infrastructure.Configuration;

namespace TFlexDrawingService.Api.Security;

public enum ConfiguredUserUpsertResult
{
    Succeeded,
    LastEnabledAdminWouldBeRemoved,
    UserNameReserved
}

public sealed class ConfiguredUserStore(
    IOptions<DrawingStorageOptions> storageOptions,
    IOptionsMonitor<SecurityOptions> securityOptions,
    ILogger<ConfiguredUserStore> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly DrawingStorageOptions _storageOptions = storageOptions.Value;

    public async Task<int> InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_storageOptions.DatabasePath) ?? _storageOptions.RootPath);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS SecurityUsers (
                    UserName TEXT PRIMARY KEY COLLATE NOCASE,
                    DisplayName TEXT NOT NULL,
                    PasswordHash TEXT NOT NULL,
                    RolesJson TEXT NOT NULL,
                    Enabled INTEGER NOT NULL,
                    ApprovalStatus TEXT NOT NULL DEFAULT 'Approved',
                    RequestedAt TEXT NULL,
                    ApprovedAt TEXT NULL,
                    ApprovedByUserName TEXT NULL,
                    DeletedAt TEXT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await EnsureColumnExistsAsync(connection, "ApprovalStatus", "TEXT NOT NULL DEFAULT 'Approved'", cancellationToken);
        await EnsureColumnExistsAsync(connection, "RequestedAt", "TEXT NULL", cancellationToken);
        await EnsureColumnExistsAsync(connection, "ApprovedAt", "TEXT NULL", cancellationToken);
        await EnsureColumnExistsAsync(connection, "ApprovedByUserName", "TEXT NULL", cancellationToken);
        await EnsureColumnExistsAsync(connection, "DeletedAt", "TEXT NULL", cancellationToken);

        foreach (var user in securityOptions.CurrentValue.Users)
        {
            await InsertBootstrapUserIfMissingAsync(connection, user, cancellationToken);
        }

        var count = await CountUsersAsync(connection, cancellationToken);
        logger.LogInformation("Security user store initialized with {UserCount} user(s).", count);
        return count;
    }

    public async Task<ConfiguredUser?> ValidateCredentialsAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        var user = await FindUserAsync(userName, cancellationToken);
        if (user is null || !user.Enabled || !IsApproved(user))
        {
            return null;
        }

        return PasswordHashing.VerifyPassword(password, user.PasswordHash)
            ? user
            : null;
    }

    public async Task<ConfiguredUser?> FindUserAsync(
        string userName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT UserName, DisplayName, PasswordHash, RolesJson, Enabled
                 , ApprovalStatus, RequestedAt, ApprovedAt, ApprovedByUserName
            FROM SecurityUsers
            WHERE UserName = $userName
              AND DeletedAt IS NULL;
            """;
        command.Parameters.AddWithValue("$userName", userName.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapUser(reader) : null;
    }

    public async Task<IReadOnlyList<ConfiguredUser>> ListUsersAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT UserName, DisplayName, PasswordHash, RolesJson, Enabled
                 , ApprovalStatus, RequestedAt, ApprovedAt, ApprovedByUserName
            FROM SecurityUsers
            WHERE DeletedAt IS NULL
            ORDER BY UserName;
            """;

        var users = new List<ConfiguredUser>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            users.Add(MapUser(reader));
        }

        return users;
    }

    public async Task UpsertUserAsync(
        ConfiguredUser user,
        CancellationToken cancellationToken = default)
    {
        _ = await UpsertUserCoreAsync(user, preserveLastEnabledAdmin: false, cancellationToken);
    }

    public Task<ConfiguredUserUpsertResult> TryUpsertUserPreservingLastAdminAsync(
        ConfiguredUser user,
        CancellationToken cancellationToken = default)
    {
        return UpsertUserCoreAsync(user, preserveLastEnabledAdmin: true, cancellationToken);
    }

    public async Task<(bool Created, string Status)> RegisterPendingUserAsync(
        string userName,
        string displayName,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SecurityUsers (
                UserName, DisplayName, PasswordHash, RolesJson, Enabled,
                ApprovalStatus, RequestedAt, ApprovedAt, ApprovedByUserName,
                CreatedAt, UpdatedAt
            )
            VALUES (
                $userName, $displayName, $passwordHash, $rolesJson, 0,
                'Pending', $requestedAt, NULL, NULL,
                $createdAt, $updatedAt
            )
            ON CONFLICT(UserName) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$userName", userName.Trim());
        command.Parameters.AddWithValue(
            "$displayName",
            string.IsNullOrWhiteSpace(displayName) ? userName.Trim() : displayName.Trim());
        command.Parameters.AddWithValue("$passwordHash", PasswordHashing.HashPassword(password));
        command.Parameters.AddWithValue("$rolesJson", JsonSerializer.Serialize(new[] { "Viewer" }, JsonOptions));
        command.Parameters.AddWithValue("$requestedAt", FormatDate(now));
        command.Parameters.AddWithValue("$createdAt", FormatDate(now));
        command.Parameters.AddWithValue("$updatedAt", FormatDate(now));

        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        if (rows > 0)
        {
            return (true, "Pending");
        }

        return (false, await GetRegistrationConflictStatusAsync(connection, userName, cancellationToken));
    }

    public async Task<bool> ApproveUserAsync(
        string userName,
        string approvedByUserName,
        IReadOnlyList<string>? roles = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return false;
        }

        var effectiveRoles = NormalizeRoles(roles is { Count: > 0 } ? roles : ["Operator", "Viewer"]);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SecurityUsers
            SET Enabled = 1,
                ApprovalStatus = 'Approved',
                RolesJson = $rolesJson,
                ApprovedAt = $approvedAt,
                ApprovedByUserName = $approvedByUserName,
                UpdatedAt = $updatedAt
            WHERE UserName = $userName
              AND DeletedAt IS NULL
              AND ApprovalStatus IN ('Pending', 'Rejected');
            """;
        var now = DateTimeOffset.UtcNow;
        command.Parameters.AddWithValue("$rolesJson", JsonSerializer.Serialize(effectiveRoles, JsonOptions));
        command.Parameters.AddWithValue("$approvedAt", FormatDate(now));
        command.Parameters.AddWithValue("$approvedByUserName", approvedByUserName);
        command.Parameters.AddWithValue("$updatedAt", FormatDate(now));
        command.Parameters.AddWithValue("$userName", userName.Trim());
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> RejectUserAsync(
        string userName,
        string rejectedByUserName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return false;
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SecurityUsers
            SET Enabled = 0,
                ApprovalStatus = 'Rejected',
                ApprovedAt = $approvedAt,
                ApprovedByUserName = $approvedByUserName,
                UpdatedAt = $updatedAt
            WHERE UserName = $userName
              AND DeletedAt IS NULL
              AND NOT (
                  Enabled = 1
                  AND ApprovalStatus = 'Approved' COLLATE NOCASE
                  AND RolesJson LIKE '%"Admin"%' COLLATE NOCASE
              );
            """;
        var now = DateTimeOffset.UtcNow;
        command.Parameters.AddWithValue("$approvedAt", FormatDate(now));
        command.Parameters.AddWithValue("$approvedByUserName", rejectedByUserName);
        command.Parameters.AddWithValue("$updatedAt", FormatDate(now));
        command.Parameters.AddWithValue("$userName", userName.Trim());
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> SetUserEnabledAsync(
        string userName,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return false;
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SecurityUsers
            SET Enabled = $enabled,
                ApprovalStatus = CASE WHEN $enabled = 1 THEN 'Approved' ELSE ApprovalStatus END,
                UpdatedAt = $updatedAt
            WHERE UserName = $userName
              AND DeletedAt IS NULL;
            """;
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$userName", userName.Trim());

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> DeleteUserAsync(
        string userName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return false;
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SecurityUsers
            SET Enabled = 0,
                ApprovalStatus = 'Deleted',
                DeletedAt = $deletedAt,
                UpdatedAt = $deletedAt
            WHERE UserName = $userName
              AND DeletedAt IS NULL
              AND NOT (
                  Enabled = 1
                  AND ApprovalStatus = 'Approved' COLLATE NOCASE
                  AND RolesJson LIKE '%"Admin"%' COLLATE NOCASE
              );
            """;
        command.Parameters.AddWithValue("$deletedAt", FormatDate(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$userName", userName.Trim());

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private async Task InsertBootstrapUserIfMissingAsync(
        SqliteConnection connection,
        ConfiguredUser user,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user.UserName) || string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SecurityUsers (
                UserName, DisplayName, PasswordHash, RolesJson, Enabled,
                ApprovalStatus, RequestedAt, ApprovedAt, ApprovedByUserName,
                CreatedAt, UpdatedAt
            )
            SELECT $userName, $displayName, $passwordHash, $rolesJson, $enabled,
                   $approvalStatus, $requestedAt, $approvedAt, $approvedByUserName,
                   $createdAt, $updatedAt
            WHERE NOT EXISTS (
                SELECT 1 FROM SecurityUsers WHERE UserName = $userName
            );
            """;
        command.Parameters.AddWithValue("$userName", user.UserName.Trim());
        command.Parameters.AddWithValue(
            "$displayName",
            string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName.Trim() : user.DisplayName.Trim());
        command.Parameters.AddWithValue("$passwordHash", user.PasswordHash);
        command.Parameters.AddWithValue("$rolesJson", JsonSerializer.Serialize(NormalizeRoles(user.Roles), JsonOptions));
        command.Parameters.AddWithValue("$enabled", user.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$approvalStatus", NormalizeApprovalStatus(user.ApprovalStatus));
        command.Parameters.AddWithValue("$requestedAt", ToDbValue(user.RequestedAt));
        command.Parameters.AddWithValue("$approvedAt", ToDbValue(user.ApprovedAt ?? DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$approvedByUserName", ToDbValue(user.ApprovedByUserName ?? "bootstrap"));
        command.Parameters.AddWithValue("$createdAt", now);
        command.Parameters.AddWithValue("$updatedAt", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<ConfiguredUserUpsertResult> UpsertUserCoreAsync(
        ConfiguredUser user,
        bool preserveLastEnabledAdmin,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(user.UserName);
        ArgumentException.ThrowIfNullOrWhiteSpace(user.PasswordHash);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);

        if (preserveLastEnabledAdmin
            && await WouldRemoveLastEnabledAdminAsync(connection, transaction, user, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return ConfiguredUserUpsertResult.LastEnabledAdminWouldBeRemoved;
        }

        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO SecurityUsers (
                UserName, DisplayName, PasswordHash, RolesJson, Enabled,
                ApprovalStatus, RequestedAt, ApprovedAt, ApprovedByUserName,
                CreatedAt, UpdatedAt
            )
            VALUES (
                $userName, $displayName, $passwordHash, $rolesJson, $enabled,
                $approvalStatus, $requestedAt, $approvedAt, $approvedByUserName,
                $createdAt, $updatedAt
            )
            ON CONFLICT(UserName) DO UPDATE SET
                DisplayName = excluded.DisplayName,
                PasswordHash = excluded.PasswordHash,
                RolesJson = excluded.RolesJson,
                Enabled = excluded.Enabled,
                ApprovalStatus = excluded.ApprovalStatus,
                RequestedAt = excluded.RequestedAt,
                ApprovedAt = excluded.ApprovedAt,
                ApprovedByUserName = excluded.ApprovedByUserName,
                UpdatedAt = excluded.UpdatedAt
            WHERE SecurityUsers.DeletedAt IS NULL;
            """;
        command.Parameters.AddWithValue("$userName", user.UserName.Trim());
        command.Parameters.AddWithValue(
            "$displayName",
            string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName.Trim() : user.DisplayName.Trim());
        command.Parameters.AddWithValue("$passwordHash", user.PasswordHash);
        command.Parameters.AddWithValue("$rolesJson", JsonSerializer.Serialize(NormalizeRoles(user.Roles), JsonOptions));
        command.Parameters.AddWithValue("$enabled", user.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$approvalStatus", NormalizeApprovalStatus(user.ApprovalStatus));
        command.Parameters.AddWithValue("$requestedAt", ToDbValue(user.RequestedAt));
        command.Parameters.AddWithValue("$approvedAt", ToDbValue(user.ApprovedAt));
        command.Parameters.AddWithValue("$approvedByUserName", ToDbValue(user.ApprovedByUserName));
        command.Parameters.AddWithValue("$createdAt", now);
        command.Parameters.AddWithValue("$updatedAt", now);

        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ConfiguredUserUpsertResult.UserNameReserved;
        }

        await transaction.CommitAsync(cancellationToken);
        return ConfiguredUserUpsertResult.Succeeded;
    }

    private static async Task<bool> WouldRemoveLastEnabledAdminAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ConfiguredUser updatedUser,
        CancellationToken cancellationToken)
    {
        string? currentRolesJson = null;
        var currentEnabled = false;
        string? currentApprovalStatus = null;

        await using (var currentCommand = connection.CreateCommand())
        {
            currentCommand.Transaction = transaction;
            currentCommand.CommandText = """
                SELECT RolesJson, Enabled, ApprovalStatus
                FROM SecurityUsers
                WHERE UserName = $userName
                  AND DeletedAt IS NULL;
                """;
            currentCommand.Parameters.AddWithValue("$userName", updatedUser.UserName.Trim());

            await using var reader = await currentCommand.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                currentRolesJson = reader.GetString(0);
                currentEnabled = reader.GetInt32(1) == 1;
                currentApprovalStatus = reader.GetString(2);
            }
        }

        var currentIsAdmin = currentRolesJson is not null
            && IsActiveAdmin(currentEnabled, currentApprovalStatus, DeserializeRoles(currentRolesJson));
        var updatedIsAdmin = IsActiveAdmin(
            updatedUser.Enabled,
            updatedUser.ApprovalStatus,
            NormalizeRoles(updatedUser.Roles));
        if (!currentIsAdmin || updatedIsAdmin)
        {
            return false;
        }

        var enabledAdminCount = 0;
        await using var countCommand = connection.CreateCommand();
        countCommand.Transaction = transaction;
        countCommand.CommandText = """
            SELECT RolesJson
            FROM SecurityUsers
            WHERE Enabled = 1
              AND ApprovalStatus = 'Approved' COLLATE NOCASE
              AND DeletedAt IS NULL;
            """;
        await using var adminReader = await countCommand.ExecuteReaderAsync(cancellationToken);
        while (await adminReader.ReadAsync(cancellationToken))
        {
            if (DeserializeRoles(adminReader.GetString(0))
                .Contains("Admin", StringComparer.OrdinalIgnoreCase))
            {
                enabledAdminCount++;
            }
        }

        return enabledAdminCount <= 1;
    }

    private static async Task<int> CountUsersAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM SecurityUsers WHERE Enabled = 1 AND DeletedAt IS NULL;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static async Task<string> GetRegistrationConflictStatusAsync(
        SqliteConnection connection,
        string userName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CASE WHEN DeletedAt IS NULL THEN ApprovalStatus ELSE 'Deleted' END
            FROM SecurityUsers
            WHERE UserName = $userName;
            """;
        command.Parameters.AddWithValue("$userName", userName.Trim());
        return Convert.ToString(
                await command.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture)
            ?? "Existing";
    }

    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection($"Data Source={_storageOptions.DatabasePath}");
    }

    private static ConfiguredUser MapUser(SqliteDataReader reader)
    {
        return new ConfiguredUser
        {
            UserName = reader.GetString(0),
            DisplayName = reader.GetString(1),
            PasswordHash = reader.GetString(2),
            Roles = DeserializeRoles(reader.GetString(3)),
            Enabled = reader.GetInt32(4) == 1,
            ApprovalStatus = reader.GetString(5),
            RequestedAt = ReadNullableDate(reader, 6),
            ApprovedAt = ReadNullableDate(reader, 7),
            ApprovedByUserName = reader.IsDBNull(8) ? null : reader.GetString(8)
        };
    }

    private static async Task EnsureColumnExistsAsync(
        SqliteConnection connection,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(SecurityUsers);";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        await using (var alterCommand = connection.CreateCommand())
        {
            alterCommand.CommandText = $"ALTER TABLE SecurityUsers ADD COLUMN {columnName} {columnDefinition};";
            await alterCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static List<string> DeserializeRoles(string rolesJson)
    {
        try
        {
            return NormalizeRoles(JsonSerializer.Deserialize<List<string>>(rolesJson, JsonOptions) ?? []);
        }
        catch (JsonException)
        {
            return ["Viewer"];
        }
    }

    private static List<string> NormalizeRoles(IEnumerable<string> roles)
    {
        var normalized = roles
            .Select(role => role.Trim())
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.ToLowerInvariant() switch
            {
                "admin" => "Admin",
                "operator" => "Operator",
                "viewer" => "Viewer",
                _ => role
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return normalized.Count == 0 ? ["Viewer"] : normalized;
    }

    private static string NormalizeApprovalStatus(string? status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "pending" => "Pending",
            "rejected" => "Rejected",
            "deleted" => "Deleted",
            _ => "Approved"
        };
    }

    private static bool IsApproved(ConfiguredUser user)
    {
        return string.Equals(
            NormalizeApprovalStatus(user.ApprovalStatus),
            "Approved",
            StringComparison.Ordinal);
    }

    private static bool IsActiveAdmin(
        bool enabled,
        string? approvalStatus,
        IReadOnlyCollection<string> roles)
    {
        return enabled
            && string.Equals(
                NormalizeApprovalStatus(approvalStatus),
                "Approved",
                StringComparison.Ordinal)
            && roles.Contains("Admin", StringComparer.OrdinalIgnoreCase);
    }

    private static string FormatDate(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ReadNullableDate(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static object ToDbValue(DateTimeOffset? value)
    {
        return value.HasValue ? FormatDate(value.Value) : DBNull.Value;
    }

    private static object ToDbValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    }
}
