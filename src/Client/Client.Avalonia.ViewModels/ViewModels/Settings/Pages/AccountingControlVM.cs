using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using Client.Application.Ports;
using Client.Application.Settings;
using Client.Application.Settings.Search;
using Client.Avalonia.Localization;
using Client.Core.Settings;
using Client.Services;
using Client.ViewModels.Dialogs;
using ReactiveUI;

using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Settings.Pages;

public class AccountingControlVM : ReactiveObject, ISettingVM, IDisposable
{
    public string Name => SettingText.TabName("accounting");
    public SettingTabType Type => SettingTabType.Accounting;

    public IReadOnlyList<SettingAnchor> Anchors =>
    [
        new("accounting.database", "settings.accounting.section.database"),
        new("accounting.savedServers", "settings.accounting.section.savedServers"),
        new("accounting.trustedCertificates", "settings.accounting.section.trustedCertificates")
    ];

    public bool IsEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = true;

    public IReadOnlyList<SettingSearchEntry> SearchEntries => SettingText.Entries("accounting",
        ("database", ["encryptDatabase", "keyringPassword", "changePassword", "lockDatabase"]),
        ("savedServers", ["savedServers"]),
        ("trustedCertificates", ["trustedCertificates"]));

    public bool IsDatabaseEncryptionEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsDatabasePasswordSaveEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsDatabaseLockEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>Whether the system has a keyring to keep the password of the database in.</summary>
    public bool CanStorePassword => _databaseSecurity.CanStorePassword;

    /// <summary>
    /// The system has no keyring and the database is not encrypted: server passwords and device keys lie in the file
    /// as they are.
    /// </summary>
    public bool AreSecretsUnprotected => !_accountingService.AreSecretsProtected;

    /// <summary>Whether the system can hold the database file locked against other processes.</summary>
    public bool CanLockFile => _databaseSecurity.CanLockFile;

    /// <summary>The database is being encrypted or decrypted: its settings wait.</summary>
    public bool IsDatabaseBusy
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ObservableCollection<SavedCoordinatorModel> CoordinatorSessions { get; } = [];
    public ObservableCollection<TrustedCertificateItem> TrustedCertificates { get; } = [];

    public SettingsListPageVM<SavedCoordinatorModel> ServersPage { get; }
    public SettingsListPageVM<TrustedCertificateItem> CertificatesPage { get; }

    public ReactiveCommand<SavedCoordinatorModel, RxVoid> DeleteCoordinatorCommand { get; init; }
    public ReactiveCommand<SavedUserModel, RxVoid> DeleteIdentityCommand { get; }
    public ReactiveCommand<TrustedCertificateItem, RxVoid> ForgetCertificateCommand { get; init; }
    public ReactiveCommand<RxVoid, RxVoid> ChangeDatabasePasswordCommand { get; }
    public Interaction<MessageBoxVM, Result> ShowMessageBox { get; } = new();
    public Interaction<DatabasePasswordVM, bool> AskDatabasePassword { get; } = new();
    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; init; }

    private readonly IAccountingService _accountingService;
    private readonly IDatabaseSecurityService _databaseSecurity;
    private bool _isUpdating = true;

    private readonly Client.Application.Sync.IAccountBackupService? _backups;

    /// <summary>Asks where to write a backup file, by its suggested name.</summary>
    public Interaction<string, System.IO.Stream?> PickBackupTarget { get; } = new();

    /// <summary>Asks for a backup file to restore.</summary>
    public Interaction<RxVoid, System.IO.Stream?> PickBackupSource { get; } = new();

    /// <summary>Asks for the folder backups are shared through; null keeps the one there is.</summary>
    public Interaction<RxVoid, string?> PickSyncFolder { get; } = new();

    /// <summary>Asks for the recovery phrase a backup needs; null gives it up.</summary>
    public Interaction<RxVoid, string?> AskRecoveryPhrase { get; } = new();

    public ReactiveCommand<SavedUserModel, RxVoid> ExportBackupCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ImportBackupCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ChooseSyncFolderCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> StopSyncCommand { get; }

    public bool AreBackupsAvailable => _backups != null;

    /// <summary>The folder backups are shared through with the other devices; null when syncing is off.</summary>
    public string? SyncFolder
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsSyncOn));
        }
    }

    public bool IsSyncOn => SyncFolder != null;

    public AccountingControlVM(IAccountingService accountingService, IDatabaseSecurityService databaseSecurity,
        Client.Application.Sync.IAccountBackupService? backups = null)
    {
        _accountingService = accountingService;
        _databaseSecurity = databaseSecurity;
        _backups = backups;
        ExportBackupCommand = ReactiveCommand.CreateFromTask<SavedUserModel>(ExportBackupAsync);
        ImportBackupCommand = ReactiveCommand.CreateFromTask(ImportBackupAsync);
        ChooseSyncFolderCommand = ReactiveCommand.CreateFromTask(ChooseSyncFolderAsync);
        StopSyncCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (_backups == null)
                return;
            await _backups.SetSyncFolderAsync(null);
            SyncFolder = null;
        });

        ServersPage = new(CoordinatorSessions, (server, filter) =>
            Contains(server.Name, filter) || Contains($"{server.Address}:{server.Port}", filter) ||
            Contains(server.CoordinatorId, filter) || server.Users.Any(user => Contains(user.Username, filter)));
        CertificatesPage = new(TrustedCertificates, (certificate, filter) =>
            Contains(certificate.Address, filter) || Contains(certificate.Subject, filter) ||
            Contains(certificate.Coordinator, filter) || Contains(certificate.Certificate.Fingerprint, filter) ||
            Contains(certificate.Fingerprint, filter));

        DeleteCoordinatorCommand = ReactiveCommand.CreateFromTask<SavedCoordinatorModel>(DeleteCoordinator);
        DeleteIdentityCommand = ReactiveCommand.CreateFromTask<SavedUserModel>(DeleteIdentity);

        ForgetCertificateCommand = ReactiveCommand.CreateFromTask<TrustedCertificateItem>(ForgetCertificate);

        ChangeDatabasePasswordCommand = ReactiveCommand.CreateFromTask(ChangeDatabasePasswordAsync,
                this.WatchProperties(nameof(IsDatabaseEncryptionEnabled), nameof(IsDatabaseBusy))
                    .Select(vm => vm.IsDatabaseEncryptionEnabled && !vm.IsDatabaseBusy));

        LoadCommand = ReactiveCommand.CreateFromTask(LoadAsync);

        // Each change is a dialog or a rewrite of the file: one at a time, and the box shows the
        // outcome rather than the click
        OnUserChange(nameof(IsDatabaseEncryptionEnabled), vm => vm.IsDatabaseEncryptionEnabled,
            isEnabled => isEnabled ? EncryptDatabaseAsync() : DecryptDatabaseAsync());
        OnUserChange(nameof(IsDatabasePasswordSaveEnabled), vm => vm.IsDatabasePasswordSaveEnabled,
            SetPasswordStoredAsync);
        OnUserChange(nameof(IsDatabaseLockEnabled), vm => vm.IsDatabaseLockEnabled,
            isLocked => _databaseSecurity.SetFileLockedAsync(isLocked));
    }

    public async Task ResetAllAsync()
    {
        // A reset of the settings neither decrypts the data nor forgets where its password is kept
        await _databaseSecurity.SetFileLockedAsync(false);
        await ApplyDatabaseSettingsAsync();
    }

    private void OnUserChange(string propertyName, Func<AccountingControlVM, bool> value, Func<bool, Task> apply)
    {
        this.WatchProperty(propertyName, value)
            .Skip(1)
            .Where(_ => !_isUpdating)
            .Select(isOn => Observable.FromAsync(async () =>
            {
                try
                {
                    await apply(isOn);
                }
                finally
                {
                    await ApplyDatabaseSettingsAsync();
                }
            }))
            .Concat()
            .Subscribe();
    }

    private Task EncryptDatabaseAsync() => SetDatabasePasswordAsync(DatabasePasswordPurpose.Create);

    private async Task ChangeDatabasePasswordAsync()
    {
        await SetDatabasePasswordAsync(DatabasePasswordPurpose.Change);
        await ApplyDatabaseSettingsAsync();
    }

    private async Task SetDatabasePasswordAsync(DatabasePasswordPurpose purpose)
    {
        var isPasswordStored = true;

        var dialog = new DatabasePasswordVM(purpose, async password =>
        {
            IsDatabaseBusy = true;

            try
            {
                await Task.Run(() => _databaseSecurity.SetPasswordAsync(password));
                return null;
            }
            catch (SecureStorageException)
            {
                // Encrypted all the same, only the keyring did not take the password
                isPasswordStored = false;
                return null;
            }
            catch (Exception)
            {
                return Strings.Get("settings.accounting.encryptionFailed");
            }
            finally
            {
                IsDatabaseBusy = false;
            }
        });

        await AskDatabasePassword.Handle(dialog);

        // Escape closes the dialog, not the encryption it started
        await dialog.Completion;

        if (!isPasswordStored)
            await ShowKeyringFailedAsync();
    }

    private async Task DecryptDatabaseAsync()
    {
        var answer = await ShowMessageBox.Handle(new MessageBoxVM(Icon.KeyQuestion, Buttons.YesNo,
            Strings.Get("settings.accounting.decrypt.question"),
            Strings.Get("settings.accounting.decrypt.title")));

        if (answer != Result.Yes)
            return;

        IsDatabaseBusy = true;

        try
        {
            await Task.Run(_databaseSecurity.DecryptAsync);
        }
        catch (Exception)
        {
            await ShowMessageBox.Handle(new MessageBoxVM(Icon.Error, Buttons.Ok,
                Strings.Get("settings.accounting.encryptionFailed"),
                Strings.Get("settings.accounting.decrypt.title")));
        }
        finally
        {
            IsDatabaseBusy = false;
        }
    }

    private async Task SetPasswordStoredAsync(bool isStored)
    {
        try
        {
            await _databaseSecurity.SetPasswordStoredAsync(isStored);
        }
        catch (SecureStorageException)
        {
            await ShowKeyringFailedAsync();
        }
    }

    private async Task ShowKeyringFailedAsync()
    {
        await ShowMessageBox.Handle(new MessageBoxVM(Icon.Warning, Buttons.Ok,
            Strings.Get("settings.accounting.keyringFailed"),
            Strings.Get("settings.accounting.keyringPassword.title")));
    }

    private async Task DeleteCoordinator(SavedCoordinatorModel coordinator)
    {
        var name = coordinator.Name ?? $"{coordinator.Address}:{coordinator.Port}";
        var consequence = coordinator.Users.Count switch
        {
            0 => Strings.Get("settings.accounting.forgetServer.noAccounts"),
            var count => Strings.Format("settings.accounting.forgetServer.accounts", ("count", count))
        };

        var answer = await ShowMessageBox.Handle(new MessageBoxVM(Icon.Question, Buttons.YesNo,
            Strings.Format("settings.accounting.forgetServer.question", ("server", name), ("consequence", consequence)),
            Strings.Get("settings.accounting.forgetServer.title")));

        if (answer != Result.Yes)
            return;

        await _accountingService.RemoveCoordinatorInstanceAsync(coordinator.CoordinatorId);
        CoordinatorSessions.Remove(coordinator);
    }

    private async Task DeleteIdentity(SavedUserModel user)
    {
        var answer = await ShowMessageBox.Handle(new MessageBoxVM(Icon.Question, Buttons.YesNo,
            Strings.Format("settings.accounting.forgetIdentity.question", ("user", user.Username)),
            Strings.Get("settings.accounting.forgetIdentity.title")));
        if (answer != Result.Yes)
            return;

        await _accountingService.RemoveUserIdentityAsync(user.CoordinatorId, user.Id);
        await LoadAsync();
    }

    private async Task ExportBackupAsync(SavedUserModel user)
    {
        if (_backups == null)
            return;

        var file = await _backups.ExportAsync(user.CoordinatorId, user.Id);
        if (file == null)
        {
            // The backup key is known once the phrase was here; otherwise the user gives it
            if (await AskRecoveryPhrase.Handle(RxVoid.Default) is not { } phrase)
                return;
            file = await _backups.ExportAsync(user.CoordinatorId, user.Id, phrase);
            if (file == null)
            {
                await ShowBackupMessage(Icon.Error, "settings.backup.phraseWrong");
                return;
            }
        }

        await using var target = await PickBackupTarget.Handle($"flowrate-{user.Username}.{Client.Application.Sync.AccountBackupFile.Extension}");
        if (target == null)
            return;
        await target.WriteAsync(file);
        await ShowBackupMessage(Icon.Info, "settings.backup.exported");
    }

    private async Task ImportBackupAsync()
    {
        if (_backups == null)
            return;

        byte[] file;
        await using (var source = await PickBackupSource.Handle(RxVoid.Default))
        {
            if (source == null)
                return;
            using var buffer = new System.IO.MemoryStream();
            await source.CopyToAsync(buffer);
            file = buffer.ToArray();
        }

        var restored = await _backups.ImportAsync(file);
        if (restored == null && await AskRecoveryPhrase.Handle(RxVoid.Default) is { } phrase)
            restored = await _backups.ImportAsync(file, phrase);

        await ShowBackupMessage(restored == null ? Icon.Error : Icon.Info,
            restored == null ? "settings.backup.importFailed" : "settings.backup.imported");
        if (restored != null)
            await LoadAsync();
    }

    private async Task ChooseSyncFolderAsync()
    {
        if (_backups == null || await PickSyncFolder.Handle(RxVoid.Default) is not { } folder)
            return;
        await _backups.SetSyncFolderAsync(folder);
        SyncFolder = folder;
    }

    private Task ShowBackupMessage(Icon icon, string key) =>
        ShowMessageBox.Handle(new MessageBoxVM(icon, Buttons.Ok, Strings.Get(key), Strings.Get("settings.backup.title")))
            .ToTask();

    private async Task ForgetCertificate(TrustedCertificateItem item)
    {
        var answer = await ShowMessageBox.Handle(new MessageBoxVM(Icon.Question, Buttons.YesNo,
            Strings.Format("settings.accounting.forgetCertificate.question", ("address", item.Address)),
            Strings.Get("settings.accounting.forgetCertificate.title")));

        if (answer != Result.Yes)
            return;

        await _accountingService.RemoveTrustedCertificateAsync(item.Certificate.Host, item.Certificate.Port);
        TrustedCertificates.Remove(item);
    }

    /// <summary>
    /// Shows the settings as they are now, which is not what was clicked when a dialog was cancelled
    /// or a change failed.
    /// </summary>
    private async Task ApplyDatabaseSettingsAsync()
    {
        var databaseSettings = await _databaseSecurity.GetSettingsAsync();

        _isUpdating = true;

        IsDatabaseEncryptionEnabled = databaseSettings.IsDatabaseEncryptionEnabled;
        IsDatabasePasswordSaveEnabled = databaseSettings.IsDatabasePasswordSaveEnabled;
        IsDatabaseLockEnabled = databaseSettings.IsDatabaseLockEnabled;

        _isUpdating = false;
    }

    private async Task LoadAsync()
    {
        if (_backups != null)
            SyncFolder = await _backups.GetSyncFolderAsync();
        await ApplyDatabaseSettingsAsync();

        var coordinators = await _accountingService.GetAllSavedCoordinatorsAsync();

        CoordinatorSessions.Clear();
        foreach (var item in coordinators)
        {
            CoordinatorSessions.Add(item);
        }

        var certificates = await _accountingService.GetTrustedCertificatesAsync();

        TrustedCertificates.Clear();
        foreach (var certificate in certificates)
        {
            var coordinatorName = coordinators.FirstOrDefault(c => c.CoordinatorId == certificate.CoordinatorId)?.Name;
            TrustedCertificates.Add(new TrustedCertificateItem(certificate, coordinatorName));
        }
    }

    private static bool Contains(string? text, string filter) =>
        text?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true;

    public void Dispose()
    {
        ServersPage.Dispose();
        CertificatesPage.Dispose();
    }

}
