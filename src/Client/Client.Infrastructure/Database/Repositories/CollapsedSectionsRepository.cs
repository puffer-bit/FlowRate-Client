using Client.Application.Ports.Repositories;
using Dapper;

namespace Client.Infrastructure.Database.Repositories;

internal sealed class CollapsedSectionsRepository : ICollapsedSectionsRepository
{
    private readonly SqliteDatabase _database;

    public CollapsedSectionsRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<IReadOnlyList<string>> GetAsync()
    {
        await using var connection = await _database.OpenAsync();

        var keys = await connection.QueryAsync<string>("SELECT SectionKey FROM CollapsedSections ORDER BY SectionKey");
        return keys.AsList();
    }

    public async Task SetAsync(string sectionKey, bool isCollapsed)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync(isCollapsed
            ? "INSERT INTO CollapsedSections (SectionKey) VALUES (@sectionKey) ON CONFLICT (SectionKey) DO NOTHING"
            : "DELETE FROM CollapsedSections WHERE SectionKey = @sectionKey", new { sectionKey });
    }
}
