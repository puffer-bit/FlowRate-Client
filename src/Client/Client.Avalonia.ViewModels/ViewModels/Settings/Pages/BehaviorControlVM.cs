using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Client.Application.Settings;
using Client.Application.Settings.Search;
using Client.Core.Settings;
using Client.Services;
using ReactiveUI;

using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Settings.Pages;

public class BehaviorControlVM : ReactiveObject, ISettingVM
{
    public string Name => SettingText.TabName("behavior");
    public SettingTabType Type => SettingTabType.AppBehavior;
    public bool IsEnabled => true;

    public IReadOnlyList<SettingSearchEntry> SearchEntries => SettingText.Entries("behavior",
        ("autoConnection", ["autoConnect", "server", "identity"]),
        ("manualConnection", ["fallbackServers", "fallbackWarning", "propagatedWarning"]),
        ("tray", ["closeAction"]),
        ("updates", ["updateCheck", "updateChannel"]));

    public bool IsAutoConnectedEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsAutoConnectedToFallbackServersEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsFallbackConnectionWarningEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsPropagatedConnectionWarningEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    // Index into the close button choices of the view, in the order of CloseButtonAction
    public int CloseButtonActionIndex
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsUpdateCheckOnStartupEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    // Index into the channel choices of the view, in the order of UpdateChannel
    public int UpdateChannelIndex
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public IReadOnlyList<SavedCoordinatorModel> SavedServers
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = [];

    public SavedCoordinatorModel? SelectedServer
    {
        get;
        set
        {
            if (ReferenceEquals(field, value))
                return;
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(Identities));
            SelectedIdentity = value?.Users.FirstOrDefault();
        }
    }

    public IReadOnlyList<SavedUserModel> Identities => SelectedServer?.Users ?? [];

    public SavedUserModel? SelectedIdentity
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; init; }

    private readonly IBehaviorService _behaviorService;
    private readonly IAccountingService _accountingService;

    // Set while the view model fills its own properties, so that only user changes get saved
    private bool _isUpdating = true;

    public BehaviorControlVM(IBehaviorService behaviorService, IAccountingService accountingService)
    {
        _behaviorService = behaviorService;

        _accountingService = accountingService;

        LoadCommand = ReactiveCommand.CreateFromTask(LoadAsync);

        ObserveSettingsChanges();

        this.WatchProperty(nameof(SelectedIdentity), x => x.SelectedIdentity)
            .Where(_ => !_isUpdating)
            .Select(identity => Observable.FromAsync(() => identity == null
                ? _behaviorService.RemoveLastIdentityAsync()
                : _behaviorService.SetLastIdentityAsync((identity.CoordinatorId, identity.Id))))
            .Concat()
            .Subscribe();
    }

    private void ObserveSettingsChanges()
    {
        // Every change saves the whole tab, one save at a time
        this.WatchProperties(
                nameof(IsAutoConnectedEnabled),
                nameof(IsAutoConnectedToFallbackServersEnabled),
                nameof(IsFallbackConnectionWarningEnabled),
                nameof(IsPropagatedConnectionWarningEnabled),
                nameof(CloseButtonActionIndex),
                nameof(IsUpdateCheckOnStartupEnabled),
                nameof(UpdateChannelIndex))
            .Where(_ => !_isUpdating)
            .Select(_ => Observable.FromAsync(() => _behaviorService.SaveSettingsAsync(ToSettings())))
            .Concat()
            .Subscribe();
    }

    private async Task LoadAsync()
    {
        _isUpdating = true;

        try
        {
            Apply(await _behaviorService.GetSettingsAsync());
            var saved = await _behaviorService.GetAutoConnectInstance();
            SavedServers = (await _accountingService.GetAllSavedCoordinatorsAsync())
                .Where(server => server.Users.Count > 0).ToList();
            SelectedServer = SavedServers.FirstOrDefault(server => server.CoordinatorId == saved?.Coordinator.CoordinatorId);
            SelectedIdentity = SelectedServer?.Users.FirstOrDefault(user => user.Id == saved?.User.Id);
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private BehaviorSettings ToSettings() => new()
    {
        IsAutoConnectEnabled = IsAutoConnectedEnabled,
        IsFallbackConnectionEnabled = IsAutoConnectedToFallbackServersEnabled,
        IsFallbackConnectionWarningEnabled = IsFallbackConnectionWarningEnabled,
        IsPropagatedConnectionWarningEnabled = IsPropagatedConnectionWarningEnabled,
        CloseButtonAction = (CloseButtonAction)CloseButtonActionIndex,
        IsUpdateCheckOnStartupEnabled = IsUpdateCheckOnStartupEnabled,
        UpdateChannel = (UpdateChannel)UpdateChannelIndex
    };

    private void Apply(BehaviorSettings settings)
    {
        IsAutoConnectedEnabled = settings.IsAutoConnectEnabled;
        IsAutoConnectedToFallbackServersEnabled = settings.IsFallbackConnectionEnabled;
        IsFallbackConnectionWarningEnabled = settings.IsFallbackConnectionWarningEnabled;
        IsPropagatedConnectionWarningEnabled = settings.IsPropagatedConnectionWarningEnabled;
        CloseButtonActionIndex = (int)settings.CloseButtonAction;
        IsUpdateCheckOnStartupEnabled = settings.IsUpdateCheckOnStartupEnabled;
        UpdateChannelIndex = (int)settings.UpdateChannel;
    }

    public async Task ResetAllAsync()
    {
        var defaults = new BehaviorSettings();

        _isUpdating = true;
        Apply(defaults);
        _isUpdating = false;

        await _behaviorService.SaveSettingsAsync(defaults);
    }
}
