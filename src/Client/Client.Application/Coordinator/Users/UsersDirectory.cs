using System.Collections.Concurrent;
using Client.Core.Users;
using Client.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Client.Application.Coordinator.Users;

/// <summary>
/// Users of one coordinator. Answers from memory when it can and asks the server only for the
/// users it does not know yet, all of them in one request.
/// </summary>
public sealed partial class UsersDirectory
{
    private readonly ICoordinatorGateway _gateway;
    private readonly ILogger<UsersDirectory> _logger;

    private readonly ConcurrentDictionary<int, UserModel> _cache = new();
    // Users the server answered without, or announced deleted; the server removes them for good
    private readonly ConcurrentDictionary<int, byte> _deleted = new();

    /// <summary>
    /// Raised with the users the server has just sent, so they can be stored outside of this cache.
    /// </summary>
    public event Action<IReadOnlyCollection<UserModel>>? UsersReceived;

    public UsersDirectory(ICoordinatorGateway gateway, ILogger<UsersDirectory> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<UserModel?> GetUserAsync(int userId)
    {
        var users = await GetUsersAsync([userId]);

        return users.GetValueOrDefault(userId);
    }

    /// <summary>
    /// Users the server does not know are missing from the result.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, UserModel>> GetUsersAsync(IReadOnlyCollection<int> userIds)
    {
        var result = new Dictionary<int, UserModel>();
        var missing = new List<int>();

        foreach (var userId in userIds.Distinct())
        {
            if (_cache.TryGetValue(userId, out var cached))
                result[userId] = cached;
            else
                missing.Add(userId);
        }

        if (missing.Count == 0)
            return result;

        var fetched = await FetchAsync(missing);

        foreach (var user in fetched)
            result[user.Id] = user;

        return result;
    }

    /// <summary>
    /// Asks the server for users whose name matches the query; the found users are cached like any other.
    /// Empty when the request failed.
    /// </summary>
    public async Task<IReadOnlyList<UserModel>> SearchAsync(string query, int limit)
    {
        try
        {
            var users = await _gateway.SearchUsers(query, limit);

            foreach (var user in users)
                _cache[user.Id] = user;

            if (users.Count > 0)
                UsersReceived?.Invoke(users);

            return users;
        }
        catch (Exception exception)
        {
            LogSearchUsersFailed(exception);
            return [];
        }
    }

    /// <summary>
    /// Stores a user the server sent on its own, for example in a notification.
    /// </summary>
    public void Upsert(UserModel user)
    {
        _cache[user.Id] = user;
        UsersReceived?.Invoke([user]);
    }

    /// <summary>
    /// Forgets a user the server has deleted and remembers that it is gone.
    /// </summary>
    public void Forget(int userId)
    {
        _cache.TryRemove(userId, out _);
        _deleted[userId] = 0;
    }

    /// <summary>
    /// The server answered without the user or announced it deleted. False for a user
    /// that has not been asked for, or whose request failed.
    /// </summary>
    public bool IsDeleted(int userId) => _deleted.ContainsKey(userId);

    /// <summary>
    /// Forgets the cached users, so the next request asks the server again. Called after a
    /// reconnect, since anything could have changed while we were away.
    /// </summary>
    public void Invalidate() => _cache.Clear();

    private async Task<List<UserModel>> FetchAsync(List<int> userIds)
    {
        var users = new List<UserModel>();

        try
        {
            var received = await _gateway.GetUsersInfo(userIds);
            if (received == null)
            {
                LogGetUsersRefused(userIds.Count);
                return users;
            }

            foreach (var user in received.Values)
            {
                _cache[user.Id] = user;
                users.Add(user);
            }

            foreach (var userId in userIds)
            {
                if (!received.ContainsKey(userId))
                    _deleted[userId] = 0;
            }
        }
        catch (Exception exception)
        {
            LogGetUsersFailed(exception, userIds.Count);
        }

        if (users.Count > 0)
            UsersReceived?.Invoke(users);

        return users;
    }
}
