using Client.Services;
using Client.Application.Settings.Search;
using System.Collections.Generic;
using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using Client.Core.Settings;
using Client.Application.Settings;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Settings.Pages;

public class BehaviorControlVM : ReactiveObject, ISettingVM
{
    public string Name => SettingText.TabName("behavior");
    public SettingTabType Type => SettingTabType.AppBehavior;
    public bool IsEnabled => true;

    public IReadOnlyList<SettingSearchEntry> SearchEntries => SettingText.Entries("behavior",
        ("autoConnection", ["autoConnect", "resetConnection"]),
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

    public bool IsSavedCoordinatorExists
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }
    
    public ReactiveCommand<RxVoid, RxVoid> ResetSavedCoordinator { get; }
    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; init; }
    
    private readonly IBehaviorService _behaviorService;
    
    // Set while the view model fills its own properties, so that only user changes get saved
    private bool _isUpdating = true;
    
    public BehaviorControlVM(IBehaviorService behaviorService)
    {
        _behaviorService = behaviorService;
        
        ResetSavedCoordinator = ReactiveCommand
            .CreateFromTask(ResetLastIdentity);
        
        LoadCommand = ReactiveCommand
            .CreateFromTask(LoadAsync);

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
        
        Apply(await _behaviorService.GetSettingsAsync());
        IsSavedCoordinatorExists = await _behaviorService.GetAutoConnectInstance() != null;
        
        _isUpdating = false;
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
    
    private async Task ResetLastIdentity()
    {
        await _behaviorService.RemoveLastIdentityAsync();
        IsSavedCoordinatorExists = false;
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
