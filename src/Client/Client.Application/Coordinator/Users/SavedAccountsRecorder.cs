using Client.Core.Users;
using Client.Application.Ports.Repositories;
using Microsoft.Extensions.Logging;

namespace Client.Application.Coordinator.Users;

/// <summary>
/// Keeps the saved account of the current user in step with the server: its name, which the server may change while
/// connected or while we were away, and its device, forgotten once the server removed it.
/// Kept apart from the session, which works without any local storage.
/// </summary>
public sealed partial class SavedAccountsRecorder
{
    private readonly IAccountingSettingsRepository _accountingSettings;
    private readonly ILogger<SavedAccountsRecorder> _logger;

    private readonly Sync.IAccountBackupService? _backups;

    public SavedAccountsRecorder(IAccountingSettingsRepository accountingSettings, ILogger<SavedAccountsRecorder> logger,
        Sync.IAccountBackupService? backups = null)
    {
        _accountingSettings = accountingSettings;
        _logger = logger;
        _backups = backups;
    }

    // Writes of one session close together are made in their order
    private readonly SemaphoreSlim _writes = new(1, 1);

    public void Track(ICoordinatorSession session)
    {
        session.CurrentUserChanged += user => _ = RenameAsync(session.CoordinatorModel!.Id, user);
        // Once the account is saved, the sync folder takes what other devices left and gets the backup of this one
        session.StateChanged += state =>
        {
            if (state == Client.Core.Coordinator.CoordinatorState.Connected && _backups != null)
                _ = WriteAsync(session, (coordinatorId, userId) => _backups.SyncAsync(coordinatorId, userId));
        };
        // The session dropped the key already; the saved account needs a login from now on
        session.DeviceEnded += reason => _ = WriteAsync(session, (coordinatorId, userId) =>
            _accountingSettings.SetDeviceAsync(coordinatorId, userId, null, null));
    }

    private async Task WriteAsync(ICoordinatorSession session, Func<string, int, Task> write)
    {
        if (session.CoordinatorModel?.Id is not { } coordinatorId)
            return;

        int userId;
        try
        {
            userId = session.GetCurrentUser().Id;
        }
        catch (InvalidOperationException)
        {
            // Not logged in yet: the account is saved by whoever connects once the login is done
            return;
        }
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
