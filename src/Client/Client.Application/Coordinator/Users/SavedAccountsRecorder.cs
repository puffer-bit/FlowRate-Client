using Client.Core.Users;
using Client.Application.Ports.Repositories;
using Microsoft.Extensions.Logging;

namespace Client.Application.Coordinator.Users;

/// <summary>
/// Keeps the name of the saved account of the current user in step with the server, which may
/// rename the user while connected or while we were away.
/// Kept apart from the session, which works without any local storage.
/// </summary>
public sealed partial class SavedAccountsRecorder
{
    private readonly IAccountingSettingsRepository _accountingSettings;
    private readonly ILogger<SavedAccountsRecorder> _logger;

    public SavedAccountsRecorder(IAccountingSettingsRepository accountingSettings, ILogger<SavedAccountsRecorder> logger)
    {
        _accountingSettings = accountingSettings;
        _logger = logger;
    }

    // The tokens of reconnects close together are written in their order
    private readonly SemaphoreSlim _writes = new(1, 1);

    public void Track(ICoordinatorSession session)
    {
        session.CurrentUserChanged += user => _ = RenameAsync(session.CoordinatorModel!.Id, user);
        session.SessionTokenChanged += token => _ = WriteAsync(session, (coordinatorId, userId) =>
            _accountingSettings.SetSessionTokenAsync(coordinatorId, userId, token));
        session.UserIdentityChanged += identity => _ = WriteAsync(session, (coordinatorId, userId) =>
            _accountingSettings.SetUserIdentityAsync(coordinatorId, userId, identity));
    }

    private async Task WriteAsync(ICoordinatorSession session, Func<string, int, Task> write)
    {
        if (session.CoordinatorModel?.Id is not { } coordinatorId)
            return;

        var userId = session.GetCurrentUser().Id;
        await _writes.WaitAsync();
        try
        {
            await write(coordinatorId, userId);
        }
        catch (Exception exception)
        {
            LogCredentialsNotSaved(exception, userId);
        }
        finally
        {
            _writes.Release();
        }
    }

    private async Task RenameAsync(string coordinatorId, UserModel user)
    {
        try
        {
            await _accountingSettings.RenameUserAsync(coordinatorId, user.Id, user.Username);
        }
        catch (Exception exception)
        {
            LogRenameFailed(exception, user.Id);
        }
    }
}
