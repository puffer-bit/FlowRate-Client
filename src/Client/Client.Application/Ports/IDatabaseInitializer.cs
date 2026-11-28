
namespace Client.Application.Ports;

public interface IDatabaseInitializer
{
    Task<DatabaseState> VerifyDatabaseAsync();

    /// <summary>
    /// Moves the database file aside, for a database this build cannot open; the next
    /// <see cref="VerifyDatabaseAsync"/> creates a new one. Nothing is deleted.
    /// </summary>
    /// <returns>Where the old file is now.</returns>
    string ResetDatabase();
}
