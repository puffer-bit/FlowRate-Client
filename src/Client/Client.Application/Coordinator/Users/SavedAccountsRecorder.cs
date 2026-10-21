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

    public void Track(ICoordinatorSession session)
    {
        session.CurrentUserChanged += user => _ = RenameAsync(session.CoordinatorModel!.Id, user);
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
