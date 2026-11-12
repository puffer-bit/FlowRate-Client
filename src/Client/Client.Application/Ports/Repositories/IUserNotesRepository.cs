namespace Client.Application.Ports.Repositories;

/// <summary>Private local notes, keyed by the coordinator instance and the user being described.</summary>
public interface IUserNotesRepository
{
    Task<string?> GetAsync(string coordinatorInstanceId, int userId);

    /// <summary>Replaces a note. An empty or whitespace-only note removes the stored entry.</summary>
    Task SetAsync(string coordinatorInstanceId, int userId, string text);
}
