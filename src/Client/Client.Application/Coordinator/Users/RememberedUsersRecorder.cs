using Client.Core.Users;
using Client.Application.Ports.Repositories;
using Microsoft.Extensions.Logging;

namespace Client.Application.Coordinator.Users;

/// <summary>
/// Stores the users a coordinator session meets, so their local settings outlive the connection.
/// Kept apart from the session, which works without any local storage.
/// </summary>
public sealed partial class RememberedUsersRecorder
{
    private readonly IRememberedUsersRepository _rememberedUsers;
    private readonly ILogger<RememberedUsersRecorder> _logger;
    private readonly Lock _queueLock = new();
    private Task _write = Task.CompletedTask;

    public RememberedUsersRecorder(IRememberedUsersRepository rememberedUsers, ILogger<RememberedUsersRecorder> logger)
    {
        _rememberedUsers = rememberedUsers;
        _logger = logger;
    }

    public void Track(ICoordinatorSession session)
    {
        session.UsersReceived += users =>
        {
            lock (_queueLock)
            {
                var previous = _write;
                _write = Task.Run(async () =>
                {
                    await previous.ConfigureAwait(false);
                    await RememberAsync(users).ConfigureAwait(false);
                });
            }
        };
    }

    private async Task RememberAsync(IEnumerable<UserModel> users)
    {
        foreach (var user in users)
        {
            try
            {
                await _rememberedUsers.SaveUserAsync(user);
            }
            catch (Exception exception)
            {
                LogRememberFailed(exception, user.Id);
            }
        }
    }
}
