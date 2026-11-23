using Client.Application.Ports;
using Dapper;

namespace Client.Infrastructure.Database.Repositories;

/// <summary>Secrets kept in the local database when the system has no keyring.</summary>
internal sealed class SecretsRepository(SqliteDatabase database) : ISecretsRepository
{
    public async Task SaveAsync(string name, string value)
    {
        await using var connection = await database.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO Secrets (Name, Value) VALUES (@name, @value)
            ON CONFLICT (Name) DO UPDATE SET Value = excluded.Value
            """, new { name, value });
    }

    public async Task<string?> LoadAsync(string name)
    {
        await using var connection = await database.OpenAsync();
        return await connection.QueryFirstOrDefaultAsync<string?>(
            "SELECT Value FROM Secrets WHERE Name = @name", new { name });
    }

    public async Task DeleteAsync(string name)
    {
        await using var connection = await database.OpenAsync();
        await connection.ExecuteAsync("DELETE FROM Secrets WHERE Name = @name", new { name });
    }

    public Task<bool> IsDatabaseEncryptedAsync() => Task.FromResult(database.Password != null);
}
