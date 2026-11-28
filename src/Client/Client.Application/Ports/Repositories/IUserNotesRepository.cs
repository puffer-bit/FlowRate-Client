namespace Client.Application.Ports.Repositories;

/// <summary>Private local notes, keyed by the coordinator instance and the user being described.</summary>
public interface IUserNotesRepository
{
    Task<string?> GetAsync(string coordinatorInstanceId, int userId);

    /// <summary>Every note of the coordinator, by the user it describes.</summary>
    Task<IReadOnlyDictionary<int, string>> GetAllAsync(string coordinatorInstanceId);

    /// <summary>Replaces a note. An empty or whitespace-only note removes the stored entry.</summary>
    Task SetAsync(string coordinatorInstanceId, int userId, string text);
}
