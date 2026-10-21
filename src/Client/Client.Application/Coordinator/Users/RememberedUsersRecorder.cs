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

    public RememberedUsersRecorder(IRememberedUsersRepository rememberedUsers, ILogger<RememberedUsersRecorder> logger)
    {
        _rememberedUsers = rememberedUsers;
        _logger = logger;
    }

    public void Track(ICoordinatorSession session)
    {
        session.UsersReceived += users => _ = RememberAsync(users);
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
