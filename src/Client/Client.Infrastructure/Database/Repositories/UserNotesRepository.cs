using Client.Application.Ports.Repositories;
using Dapper;

namespace Client.Infrastructure.Database.Repositories;

internal sealed class UserNotesRepository(SqliteDatabase database) : IUserNotesRepository
{
    public async Task<string?> GetAsync(string coordinatorInstanceId, int userId)
    {
        await using var connection = await database.OpenAsync();
        return await connection.QueryFirstOrDefaultAsync<string>("""
            SELECT Text FROM UserNotes
            WHERE CoordinatorInstanceId = @coordinatorInstanceId AND UserId = @userId
            """, new { coordinatorInstanceId, userId });
    }

    public async Task<IReadOnlyDictionary<int, string>> GetAllAsync(string coordinatorInstanceId)
    {
        await using var connection = await database.OpenAsync();
        var rows = await connection.QueryAsync<UserNoteRow>("""
            SELECT UserId, Text FROM UserNotes WHERE CoordinatorInstanceId = @coordinatorInstanceId
            """, new { coordinatorInstanceId });
        return rows.ToDictionary(row => row.UserId, row => row.Text);
    }

    public async Task SetAsync(string coordinatorInstanceId, int userId, string text)
    {
        await using var connection = await database.OpenAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            await connection.ExecuteAsync("""
                DELETE FROM UserNotes
                WHERE CoordinatorInstanceId = @coordinatorInstanceId AND UserId = @userId
                """, new { coordinatorInstanceId, userId });
            return;
        }

        await connection.ExecuteAsync("""
            INSERT INTO UserNotes (CoordinatorInstanceId, UserId, Text)
            VALUES (@coordinatorInstanceId, @userId, @text)
            ON CONFLICT (CoordinatorInstanceId, UserId) DO UPDATE SET Text = excluded.Text
            """, new { coordinatorInstanceId, userId, text });
    }
}

internal sealed record UserNoteRow(int UserId, string Text);
