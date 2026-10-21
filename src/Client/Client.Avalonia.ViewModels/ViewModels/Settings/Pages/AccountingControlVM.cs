using Client.Application.Ports;
using Client.Application.Settings;
using Client.Application.Settings.Search;
using Client.Avalonia.Localization;
using Client.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Client.Core.Settings;
using Client.ViewModels.Dialogs;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Settings.Pages;

public class AccountingControlVM : ReactiveObject, ISettingVM
{
    public string Name => SettingText.TabName("accounting");
    public SettingTabType Type => SettingTabType.Accounting;

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

    /// <summary>Whether the system can hold the database file locked against other processes.</summary>
    public bool CanLockFile => _databaseSecurity.CanLockFile;

    /// <summary>The database is being encrypted or decrypted: its settings wait.</summary>
    public bool IsDatabaseBusy
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ObservableCollection<SavedCoordinatorModel> CoordinatorSessions { get; set; } = [];
    public ObservableCollection<TrustedCertificateItem> TrustedCertificates { get; } = [];
    
    public ReactiveCommand<SavedCoordinatorModel, RxVoid> DeleteIdentityCommand { get; init; }
    public ReactiveCommand<TrustedCertificateItem, RxVoid> ForgetCertificateCommand { get; init; }
    public ReactiveCommand<RxVoid, RxVoid> ChangeDatabasePasswordCommand { get; }
    public Interaction<MessageBoxVM, Result> ShowMessageBox { get; } = new();
    public Interaction<DatabasePasswordVM, bool> AskDatabasePassword { get; } = new();
    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; init; }
    
    private readonly IAccountingService _accountingService;
    private readonly IDatabaseSecurityService _databaseSecurity;
    private bool _isUpdating = true;
    
    public AccountingControlVM(IAccountingService accountingService, IDatabaseSecurityService databaseSecurity)
    {
        _accountingService = accountingService;
        _databaseSecurity = databaseSecurity;
        
        DeleteIdentityCommand = ReactiveCommand
            .CreateFromTask<SavedCoordinatorModel>(DeleteIdentity);

        ForgetCertificateCommand = ReactiveCommand
            .CreateFromTask<TrustedCertificateItem>(ForgetCertificate);

        ChangeDatabasePasswordCommand = ReactiveCommand
            .CreateFromTask(ChangeDatabasePasswordAsync,
                this.WatchProperties(nameof(IsDatabaseEncryptionEnabled), nameof(IsDatabaseBusy))
                    .Select(vm => vm.IsDatabaseEncryptionEnabled && !vm.IsDatabaseBusy));
        
        LoadCommand = ReactiveCommand
            .CreateFromTask(LoadAsync);

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

    private async Task DeleteIdentity(SavedCoordinatorModel coordinator)
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
}