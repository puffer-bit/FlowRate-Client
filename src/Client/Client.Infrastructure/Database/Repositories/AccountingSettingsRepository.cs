using Client.Application.Ports.Repositories;
using Client.Core.Settings;
using Dapper;

namespace Client.Infrastructure.Database.Repositories;

internal sealed class AccountingSettingsRepository : IAccountingSettingsRepository
{
    private const string SelectCoordinators =
        "SELECT CoordinatorId, Name, Address, Port FROM SavedCoordinators";

    // The avatar is what the remembered users know about the same user
    private const string SelectIdentities = """
        SELECT CoordinatorId, UserId, UserIdentity, Username, Password,
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

    public async Task<SavedCoordinatorModel?> GetCoordinatorAsync(string address, int port)
    {
        await using var connection = await _database.OpenAsync();

        var coordinator = await connection.QueryFirstOrDefaultAsync<SavedCoordinatorRow>(
            SelectCoordinators + " WHERE Address = @address AND Port = @port", new { address, port });

        return coordinator == null ? null : await WithIdentitiesAsync(connection, coordinator);
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

        await connection.ExecuteAsync("""
            INSERT INTO SavedIdentities (CoordinatorId, UserId, UserIdentity, Username, Password)
            VALUES (@CoordinatorId, @Id, @UserIdentity, @Username, @Password)
            ON CONFLICT (CoordinatorId, UserId) DO UPDATE SET
                UserIdentity = excluded.UserIdentity,
                Username = excluded.Username,
                Password = excluded.Password
            """, new { model.CoordinatorId, model.Id, model.UserIdentity, model.Username, model.Password });
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
            INSERT INTO SavedCoordinators (CoordinatorId, Name, Address, Port)
            VALUES (@CoordinatorId, @Name, @Address, @Port)
            ON CONFLICT (CoordinatorId) DO UPDATE SET
                Name = excluded.Name,
                Address = excluded.Address,
                Port = excluded.Port
            """, new { model.CoordinatorId, model.Name, model.Address, model.Port });
    }

    public async Task DeleteCoordinatorAsync(string coordinatorId)
    {
        await using var connection = await _database.OpenAsync();

        // The identities go with it through the foreign key
        await connection.ExecuteAsync(
            "DELETE FROM SavedCoordinators WHERE CoordinatorId = @coordinatorId", new { coordinatorId });
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

internal sealed record SavedCoordinatorRow(string CoordinatorId, string? Name, string Address, int Port)
{
    public SavedCoordinatorModel ToModel(IEnumerable<SavedIdentityRow> identities) => new()
    {
        CoordinatorId = CoordinatorId,
        Name = Name,
        Address = Address,
        Port = Port,
        Users = identities.Select(identity => identity.ToModel()).ToList()
    };
}

internal sealed record SavedIdentityRow(string CoordinatorId, int UserId, string UserIdentity, string Username,
    string? Password, string? AvatarUrl)
{
    public SavedUserModel ToModel() => new()
    {
        Id = UserId,
        CoordinatorId = CoordinatorId,
        UserIdentity = UserIdentity,
        Username = Username,
        Password = Password,
        AvatarUrl = AvatarUrl
    };
}

internal sealed record DatabaseSettingsRow(bool IsDatabaseEncryptionEnabled, bool IsDatabasePasswordSaveEnabled,
    bool IsDatabaseLockEnabled);

