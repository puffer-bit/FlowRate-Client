using Client.Application.Ports.Repositories;
using Dapper;

namespace Client.Infrastructure.Database.Repositories;

internal sealed class DismissedPromptsRepository : IDismissedPromptsRepository
{
    private readonly SqliteDatabase _database;

    public DismissedPromptsRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<bool> IsDismissedAsync(string promptKey)
    {
        await using var connection = await _database.OpenAsync();

        return await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM DismissedPrompts WHERE PromptKey = @promptKey", new { promptKey }) > 0;
    }

    public async Task SetAsync(string promptKey, bool isDismissed)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync(isDismissed
            ? "INSERT INTO DismissedPrompts (PromptKey) VALUES (@promptKey) ON CONFLICT (PromptKey) DO NOTHING"
            : "DELETE FROM DismissedPrompts WHERE PromptKey = @promptKey", new { promptKey });
    }
}
