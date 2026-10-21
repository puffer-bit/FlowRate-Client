using Client.Application.Ports.Repositories;
using Client.Core.Users;
using Dapper;

namespace Client.Infrastructure.Database.Repositories;

internal sealed class RememberedUsersRepository : IRememberedUsersRepository
{
    private const string SelectHybridSessionData =
        "SELECT UserId, Volume, IsVolumeOverrideEnabled, IsMuted, ScreenVolume, IsScreenVolumeOverrideEnabled, " +
        "IsScreenMuted, IsBlocked FROM RememberedUsers";

    private readonly SqliteDatabase _database;

    public RememberedUsersRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task SaveUserAsync(UserModel user)
    {
        await using var connection = await _database.OpenAsync();

        // The hybrid session data of a remembered user is left as it is
        await connection.ExecuteAsync("""
            INSERT INTO RememberedUsers (CoordinatorInstanceId, UserId, Username, Ip, RegionName, RegionCode,
                DisplayName, Prefix, AvatarUrl, State, Status, FirstConnectionTime, LastConnectionTime)
            VALUES (@CoordinatorInstanceId, @Id, @Username, @Ip, @RegionName, @RegionCode,
                @DisplayName, @Prefix, @AvatarUrl, @State, @Status, @FirstConnectionTime, @LastConnectionTime)
            ON CONFLICT (CoordinatorInstanceId, UserId) DO UPDATE SET
                Username = excluded.Username,
                Ip = excluded.Ip,
                RegionName = excluded.RegionName,
                RegionCode = excluded.RegionCode,
                DisplayName = excluded.DisplayName,
                Prefix = excluded.Prefix,
                AvatarUrl = excluded.AvatarUrl,
                State = excluded.State,
                Status = excluded.Status,
                FirstConnectionTime = excluded.FirstConnectionTime,
                LastConnectionTime = excluded.LastConnectionTime
            """, new
            {
                user.CoordinatorInstanceId,
                user.Id,
                user.Username,
                user.Ip,
                user.RegionName,
                user.RegionCode,
                user.DisplayName,
                user.Prefix,
                user.AvatarUrl,
                State = (int)user.State,
                Status = (int)user.Status,
                FirstConnectionTime = SqliteDatabase.ToStorage(user.FirstConnectionTime),
                LastConnectionTime = SqliteDatabase.ToStorage(user.LastConnectionTime)
            });
    }

    public async Task<UserHybridSessionData> GetHybridSessionDataAsync(string coordinatorInstanceId, int userId)
    {
        await using var connection = await _database.OpenAsync();

        var row = await connection.QueryFirstOrDefaultAsync<HybridSessionDataRow>(
            SelectHybridSessionData + " WHERE CoordinatorInstanceId = @coordinatorInstanceId AND UserId = @userId",
            new { coordinatorInstanceId, userId });

        return row?.ToModel() ?? new UserHybridSessionData();
    }

    public async Task<IReadOnlyDictionary<int, UserHybridSessionData>> GetHybridSessionDataAsync(
        string coordinatorInstanceId, IReadOnlyCollection<int> userIds)
    {
        await using var connection = await _database.OpenAsync();

        var stored = (await connection.QueryAsync<HybridSessionDataRow>(
                SelectHybridSessionData + " WHERE CoordinatorInstanceId = @coordinatorInstanceId AND UserId IN @userIds",
                new { coordinatorInstanceId, userIds }))
            .ToDictionary(row => row.UserId);

        return userIds
            .Distinct()
            .ToDictionary(
                id => id,
                id => stored.TryGetValue(id, out var row) ? row.ToModel() : new UserHybridSessionData());
    }

    public async Task<bool> SaveHybridSessionDataAsync(string coordinatorInstanceId, int userId, UserHybridSessionData data)
    {
        await using var connection = await _database.OpenAsync();

        var updated = await connection.ExecuteAsync("""
            UPDATE RememberedUsers SET
                Volume = @Volume,
                IsVolumeOverrideEnabled = @IsVolumeOverrideEnabled,
                IsMuted = @IsMuted,
                ScreenVolume = @ScreenVolume,
                IsScreenVolumeOverrideEnabled = @IsScreenVolumeOverrideEnabled,
                IsScreenMuted = @IsScreenMuted,
                IsBlocked = @IsBlocked
            WHERE CoordinatorInstanceId = @coordinatorInstanceId AND UserId = @userId
            """, new
            {
                coordinatorInstanceId,
                userId,
                data.Volume,
                data.IsVolumeOverrideEnabled,
                data.IsMuted,
                data.ScreenVolume,
                data.IsScreenVolumeOverrideEnabled,
                data.IsScreenMuted,
                data.IsBlocked
            });

        return updated > 0;
    }
}

internal sealed record HybridSessionDataRow(int UserId, int Volume, bool IsVolumeOverrideEnabled, bool IsMuted,
    int ScreenVolume, bool IsScreenVolumeOverrideEnabled, bool IsScreenMuted, bool IsBlocked)
{
    public UserHybridSessionData ToModel() => new()
    {
        Volume = Volume,
        IsVolumeOverrideEnabled = IsVolumeOverrideEnabled,
        IsMuted = IsMuted,
        ScreenVolume = ScreenVolume,
        IsScreenVolumeOverrideEnabled = IsScreenVolumeOverrideEnabled,
        IsScreenMuted = IsScreenMuted,
        IsBlocked = IsBlocked
    };
}
