using Client.Application.Ports.Repositories;
using Client.Core.Settings;
using Dapper;

namespace Client.Database.Sqlite.Repositories;

internal sealed class AccountingSettingsRepository : IAccountingSettingsRepository
{
    private const string SelectCoordinators =
        "SELECT CoordinatorId, Name, Address, Port, SaveOrder, LastUserId FROM SavedCoordinators";

    // The avatar is what the remembered users know about the same user
    private const string SelectIdentities = """
        SELECT CoordinatorId, UserId, UserIdentity, Username, Password, AccountPassword, SessionToken, DeviceId,
            DeviceKey, IsRecoveryPhraseSaved, LoginLostReason,
            (SELECT AvatarUrl FROM RememberedUsers
             WHERE RememberedUsers.CoordinatorInstanceId = SavedIdentities.CoordinatorId
               AND RememberedUsers.UserId = SavedIdentities.UserId) AS AvatarUrl
        FROM SavedIdentities
        """;

    private readonly SqliteDatabase _database;

    public AccountingSettingsRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<IReadOnlyList<SavedCoordinatorModel>> GetAllCoordinatorsAsync()
    {
        await using var connection = await _database.OpenAsync();

        var coordinators = (await connection.QueryAsync<SavedCoordinatorRow>(
            SelectCoordinators + " ORDER BY Address, Port")).AsList();
        var identities = (await connection.QueryAsync<SavedIdentityRow>(SelectIdentities)).AsList();

        var identitiesByCoordinator = identities.ToLookup(identity => identity.CoordinatorId);

        return coordinators
            .Select(coordinator => coordinator.ToModel(identitiesByCoordinator[coordinator.CoordinatorId]))
            .ToList();
    }

    public async Task<SavedCoordinatorModel?> GetCoordinatorAsync(string coordinatorId)
    {
        await using var connection = await _database.OpenAsync();

        var coordinator = await connection.QueryFirstOrDefaultAsync<SavedCoordinatorRow>(
            SelectCoordinators + " WHERE CoordinatorId = @coordinatorId", new { coordinatorId });

        return coordinator == null ? null : await WithIdentitiesAsync(connection, coordinator);
    }

    public async Task<(SavedCoordinatorModel Model, SavedUserModel User)?> GetCoordinatorInstanceAsync(
        (string CoordinatorId, int UserId) identityIdsPair)
    {
        var (coordinatorId, userId) = identityIdsPair;

        await using var connection = await _database.OpenAsync();

        var coordinator = await connection.QueryFirstOrDefaultAsync<SavedCoordinatorRow>(
            SelectCoordinators + " WHERE CoordinatorId = @coordinatorId", new { coordinatorId });

        if (coordinator == null)
            return null;

        var identity = await connection.QueryFirstOrDefaultAsync<SavedIdentityRow>(
            SelectIdentities + " WHERE CoordinatorId = @coordinatorId AND UserId = @userId",
            new { coordinatorId, userId });

        if (identity == null)
            return null;

        return (coordinator.ToModel([]), identity.ToModel());
    }

    public async Task SaveUserAsync(SavedUserModel model)
    {
        await using var connection = await _database.OpenAsync();

        // The server password is kept in the secret vault, never here. An account with a device has moved over: what
        // the earlier protocol logged in with goes
        var hasDevice = model.DeviceId != null;
        await connection.ExecuteAsync("""
            INSERT INTO SavedIdentities (CoordinatorId, UserId, UserIdentity, Username, Password, AccountPassword,
                SessionToken, DeviceId, DeviceKey, IsRecoveryPhraseSaved)
            VALUES (@CoordinatorId, @Id, @UserIdentity, @Username, NULL, @AccountPassword, @SessionToken, @DeviceId,
                @DeviceKey, @IsRecoveryPhraseSaved)
            ON CONFLICT (CoordinatorId, UserId) DO UPDATE SET
                UserIdentity = excluded.UserIdentity,
                Username = excluded.Username,
                Password = NULL,
                AccountPassword = excluded.AccountPassword,
                SessionToken = excluded.SessionToken,
                DeviceId = excluded.DeviceId,
                DeviceKey = excluded.DeviceKey,
                IsRecoveryPhraseSaved = excluded.IsRecoveryPhraseSaved,
                -- A device logs in again: why the earlier one was lost no longer matters
                LoginLostReason = CASE WHEN excluded.DeviceId IS NULL THEN LoginLostReason END
            """, new
        {
            model.CoordinatorId,
            model.Id,
            UserIdentity = hasDevice ? string.Empty : model.UserIdentity,
            model.Username,
            AccountPassword = hasDevice ? null : model.AccountPassword,
            SessionToken = hasDevice ? null : model.SessionToken,
            DeviceId = model.DeviceId?.ToString(),
            model.DeviceKey,
            model.IsRecoveryPhraseSaved
        });
    }

    public async Task SetDeviceAsync(string coordinatorId, int userId, Guid? deviceId, string? deviceKey,
        LoginLostReason? lostReason = null)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            UPDATE SavedIdentities SET DeviceId = @deviceId, DeviceKey = @deviceKey, LoginLostReason = @lostReason
            WHERE CoordinatorId = @coordinatorId AND UserId = @userId
            """, new
        {
            coordinatorId, userId, deviceId = deviceId?.ToString(), deviceKey,
            lostReason = deviceId == null ? (int?)lostReason : null
        });
    }

    public async Task SetLastConnectionAsync(string coordinatorId, int userId)
    {
        await using var connection = await _database.OpenAsync();
        using var transaction = connection.BeginTransaction();
        await connection.ExecuteAsync("""
            UPDATE SavedCoordinators SET LastUserId = @userId WHERE CoordinatorId = @coordinatorId;
            UPDATE BehaviorSettings SET LastCoordinatorId = @coordinatorId, LastUserId = @userId WHERE Id = 1;
            """, new { coordinatorId, userId }, transaction);
        transaction.Commit();
    }

    public async Task SetRecoveryPhraseSavedAsync(string coordinatorId, int userId, bool isSaved)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            UPDATE SavedIdentities SET IsRecoveryPhraseSaved = @isSaved
            WHERE CoordinatorId = @coordinatorId AND UserId = @userId
            """, new { coordinatorId, userId, isSaved });
    }

    public async Task ClearServerPasswordAsync(string coordinatorId, int userId)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            UPDATE SavedIdentities SET Password = NULL
            WHERE CoordinatorId = @coordinatorId AND UserId = @userId
            """, new { coordinatorId, userId });
    }

    public async Task RenameUserAsync(string coordinatorId, int userId, string username)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            UPDATE SavedIdentities SET Username = @username
            WHERE CoordinatorId = @coordinatorId AND UserId = @userId
            """, new { coordinatorId, userId, username });
    }

    public async Task SaveCoordinatorAsync(SavedCoordinatorModel model)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            INSERT INTO SavedCoordinators (CoordinatorId, Name, Address, Port, SaveOrder)
            VALUES (@CoordinatorId, @Name, @Address, @Port,
                (SELECT COALESCE(MAX(SaveOrder), 0) + 1 FROM SavedCoordinators))
            ON CONFLICT (CoordinatorId) DO UPDATE SET
                Name = excluded.Name,
                Address = excluded.Address,
                Port = excluded.Port,
                SaveOrder = excluded.SaveOrder
            """, new { model.CoordinatorId, model.Name, model.Address, model.Port });
    }

    public async Task DeleteUserAsync(string coordinatorId, int userId)
    {
        await using var connection = await _database.OpenAsync();
        using var transaction = connection.BeginTransaction();
        await connection.ExecuteAsync("""
            DELETE FROM SavedIdentities WHERE CoordinatorId = @coordinatorId AND UserId = @userId;
            UPDATE BehaviorSettings SET AutoConnectCoordinatorId = NULL, AutoConnectUserId = NULL
            WHERE AutoConnectCoordinatorId = @coordinatorId AND AutoConnectUserId = @userId;
            """, new { coordinatorId, userId }, transaction);
        transaction.Commit();
    }

    public async Task DeleteCoordinatorAsync(string coordinatorId)
    {
        await using var connection = await _database.OpenAsync();

        using var transaction = connection.BeginTransaction();
        // The identities go with it through the foreign key; a startup connection picked for it follows the last one
        await connection.ExecuteAsync("""
            DELETE FROM SavedCoordinators WHERE CoordinatorId = @coordinatorId;
            UPDATE BehaviorSettings SET AutoConnectCoordinatorId = NULL, AutoConnectUserId = NULL
            WHERE AutoConnectCoordinatorId = @coordinatorId;
            """, new { coordinatorId }, transaction);
        transaction.Commit();
    }

    public async Task<DatabaseSettings> GetDatabaseSettingsAsync()
    {
        await using var connection = await _database.OpenAsync();

        var row = await connection.QueryFirstOrDefaultAsync<DatabaseSettingsRow>("""
            SELECT IsDatabaseEncryptionEnabled, IsDatabasePasswordSaveEnabled, IsDatabaseLockEnabled 
            FROM AccountingSettings WHERE Id = 1
            """);

        if (row == null)
            return new DatabaseSettings();

        return new DatabaseSettings()
        {
            IsDatabaseEncryptionEnabled = row.IsDatabaseEncryptionEnabled,
            IsDatabasePasswordSaveEnabled = row.IsDatabasePasswordSaveEnabled,
            IsDatabaseLockEnabled = row.IsDatabaseLockEnabled
        };
    }

    public async Task UpdateDatabaseSettingsAsync(DatabaseSettings databaseSettings)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            INSERT INTO AccountingSettings (Id, IsDatabaseEncryptionEnabled, IsDatabasePasswordSaveEnabled, IsDatabaseLockEnabled) VALUES (1, @IsDatabaseEncryptionEnabled, @IsDatabasePasswordSaveEnabled, @IsDatabaseLockEnabled)
            ON CONFLICT (Id) DO UPDATE SET 
                IsDatabaseEncryptionEnabled = excluded.IsDatabaseEncryptionEnabled,
                IsDatabasePasswordSaveEnabled = excluded.IsDatabasePasswordSaveEnabled,
                IsDatabaseLockEnabled = excluded.IsDatabaseLockEnabled
            """, databaseSettings);
    }

    private static async Task<SavedCoordinatorModel> WithIdentitiesAsync(
        System.Data.Common.DbConnection connection, SavedCoordinatorRow coordinator)
    {
        var identities = await connection.QueryAsync<SavedIdentityRow>(
            SelectIdentities + " WHERE CoordinatorId = @CoordinatorId", new { coordinator.CoordinatorId });

        return coordinator.ToModel(identities);
    }
}

internal sealed record SavedCoordinatorRow(string CoordinatorId, string? Name, string Address, int Port, long SaveOrder,
    int? LastUserId)
{
    public SavedCoordinatorModel ToModel(IEnumerable<SavedIdentityRow> identities) => new()
    {
        CoordinatorId = CoordinatorId,
        Name = Name,
        Address = Address,
        Port = Port,
        SaveOrder = SaveOrder,
        LastUserId = LastUserId,
        Users = identities.Select(identity => identity.ToModel()).ToList()
    };
}

/// <param name="Password">The server password the earlier client kept here; moved to the secret vault at start.</param>
internal sealed record SavedIdentityRow(string CoordinatorId, int UserId, string UserIdentity, string Username,
    string? Password, string? AccountPassword, string? SessionToken, string? DeviceId, string? DeviceKey,
    bool IsRecoveryPhraseSaved, int? LoginLostReason, string? AvatarUrl)
{
    public SavedUserModel ToModel() => new()
    {
        Id = UserId,
        CoordinatorId = CoordinatorId,
        UserIdentity = UserIdentity,
        Username = Username,
        Password = Password,
        AccountPassword = AccountPassword,
        SessionToken = SessionToken,
        DeviceId = Guid.TryParse(DeviceId, out var deviceId) ? deviceId : null,
        DeviceKey = DeviceKey,
        IsRecoveryPhraseSaved = IsRecoveryPhraseSaved,
        LoginLostReason = (LoginLostReason?)LoginLostReason,
        AvatarUrl = AvatarUrl
    };
}

internal sealed record DatabaseSettingsRow(bool IsDatabaseEncryptionEnabled, bool IsDatabasePasswordSaveEnabled,
    bool IsDatabaseLockEnabled);

