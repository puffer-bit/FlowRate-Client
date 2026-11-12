using Client.Application.Ports.Repositories;
using Client.Core.Media;
using Dapper;

namespace Client.Infrastructure.Database.Repositories;

internal sealed class CameraPreferencesRepository(SqliteDatabase database) : ICameraPreferencesRepository
{
    public async Task<CameraPreferences> GetAsync()
    {
        await using var connection = await database.OpenAsync();
        return await connection.QueryFirstOrDefaultAsync<CameraPreferences>(
            "SELECT DeviceId, MaxHeight, Framerate, BitrateKbps, LastWasCamera FROM CameraPreferences WHERE Id = 1")
            ?? new CameraPreferences();
    }

    public async Task SaveAsync(CameraPreferences preferences)
    {
        await using var connection = await database.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO CameraPreferences (Id, DeviceId, MaxHeight, Framerate, BitrateKbps, LastWasCamera)
            VALUES (1, @DeviceId, @MaxHeight, @Framerate, @BitrateKbps, @LastWasCamera)
            ON CONFLICT (Id) DO UPDATE SET DeviceId = excluded.DeviceId, MaxHeight = excluded.MaxHeight,
                Framerate = excluded.Framerate, BitrateKbps = excluded.BitrateKbps, LastWasCamera = excluded.LastWasCamera
            """, new { preferences.DeviceId, preferences.MaxHeight, preferences.Framerate,
                preferences.BitrateKbps, preferences.LastWasCamera });
    }
}
