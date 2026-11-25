using Client.Application.Ports.Repositories;
using Dapper;

namespace Client.Database.Sqlite.Repositories;

internal sealed class PanelWidthsRepository(SqliteDatabase database) : IPanelWidthsRepository
{
    public async Task<double?> GetAsync(string panelKey)
    {
        await using var connection = await database.OpenAsync();

        return await connection.ExecuteScalarAsync<double?>(
            "SELECT Width FROM PanelWidths WHERE PanelKey = @panelKey", new { panelKey });
    }

    public async Task SetAsync(string panelKey, double width)
    {
        await using var connection = await database.OpenAsync();

        await connection.ExecuteAsync("""
            INSERT INTO PanelWidths (PanelKey, Width) VALUES (@panelKey, @width)
            ON CONFLICT (PanelKey) DO UPDATE SET Width = excluded.Width
            """, new { panelKey, width });
    }
}
