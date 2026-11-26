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
using Shared.Models.DataTransferObjects.ChatSession.Messages.Reactions;

using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Settings.Pages;

public class BehaviorControlVM : ReactiveObject, ISettingVM
{
    public string Name => SettingText.TabName("behavior");
    public SettingTabType Type => SettingTabType.AppBehavior;
    public IReadOnlyList<SettingAnchor> Anchors =>
    [
        new("behavior.playback", "settings.behavior.section.playback"),
        new("behavior.status", "settings.behavior.section.status"),
        new("behavior.autoConnection", "settings.behavior.section.autoConnection"),
        new("behavior.manualConnection", "settings.behavior.section.manualConnection"),
        new("behavior.tray", "settings.behavior.section.tray"),
        new("behavior.updates", "settings.behavior.section.updates")
    ];

    public bool IsEnabled => true;

    public IReadOnlyList<SettingSearchEntry> SearchEntries => SettingText.Entries("behavior",
        ("playback", ["keepScreenOn"]),
        ("status", ["autoIdle", "idleAfter", "sleepAfter", "keepActiveInCalls"]),
        ("autoConnection", ["autoConnect", "server", "identity"]),
        ("manualConnection", ["fallbackServers", "fallbackWarning", "propagatedWarning"]),
        ("tray", ["closeAction"]),
        ("updates", ["updateCheck", "updateChannel"]));

    public bool KeepScreenOnWhileWatching
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = true;

    /// <summary>The minutes without input after which the status turns idle, in the order of the view.</summary>
    public static IReadOnlyList<int> IdleAfterChoices { get; } = [5, 10, 15, 30];

    /// <summary>The minutes after which it turns sleeping, in the order of the view; 0 never does.</summary>
    public static IReadOnlyList<int> SleepAfterChoices { get; } = [60, 120, 240, 0];

    public bool IsAutoIdleEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = true;

    public int IdleAfterIndex
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = 1;

    public int SleepAfterIndex
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = 1;

    public bool KeepActiveDuringCalls
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = true;

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

    /// <summary>The reaction a double tap on a message puts on it; the phone shows the choice.</summary>
    public string? QuickReaction
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = BehaviorSettings.DefaultQuickReaction;

    public IReadOnlyList<string> QuickReactionChoices => ChatReactionEmoji.All;

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
                nameof(KeepScreenOnWhileWatching),
                nameof(IsAutoIdleEnabled),
                nameof(IdleAfterIndex),
                nameof(SleepAfterIndex),
                nameof(KeepActiveDuringCalls),
                nameof(IsAutoConnectedEnabled),
                nameof(IsAutoConnectedToFallbackServersEnabled),
                nameof(IsFallbackConnectionWarningEnabled),
                nameof(IsPropagatedConnectionWarningEnabled),
                nameof(CloseButtonActionIndex),
                nameof(IsUpdateCheckOnStartupEnabled),
                nameof(UpdateChannelIndex),
                nameof(QuickReaction))
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
        KeepScreenOnWhileWatching = KeepScreenOnWhileWatching,
        IsAutoIdleEnabled = IsAutoIdleEnabled,
        IdleAfterMinutes = ChoiceAt(IdleAfterChoices, IdleAfterIndex, BehaviorSettings.DefaultIdleAfterMinutes),
        SleepAfterMinutes = ChoiceAt(SleepAfterChoices, SleepAfterIndex, BehaviorSettings.DefaultSleepAfterMinutes),
        KeepActiveDuringCalls = KeepActiveDuringCalls,
        IsAutoConnectEnabled = IsAutoConnectedEnabled,
        IsFallbackConnectionEnabled = IsAutoConnectedToFallbackServersEnabled,
        IsFallbackConnectionWarningEnabled = IsFallbackConnectionWarningEnabled,
        IsPropagatedConnectionWarningEnabled = IsPropagatedConnectionWarningEnabled,
        CloseButtonAction = (CloseButtonAction)CloseButtonActionIndex,
        IsUpdateCheckOnStartupEnabled = IsUpdateCheckOnStartupEnabled,
        UpdateChannel = (UpdateChannel)UpdateChannelIndex,
        // The list clears its selection while it is rebuilt; nothing chosen keeps the default
        QuickReaction = QuickReaction is { } reaction && ChatReactionEmoji.IsSupported(reaction)
            ? reaction : BehaviorSettings.DefaultQuickReaction
    };

    private void Apply(BehaviorSettings settings)
    {
        KeepScreenOnWhileWatching = settings.KeepScreenOnWhileWatching;
        IsAutoIdleEnabled = settings.IsAutoIdleEnabled;
        IdleAfterIndex = IndexOf(IdleAfterChoices, settings.IdleAfterMinutes, BehaviorSettings.DefaultIdleAfterMinutes);
        SleepAfterIndex = IndexOf(SleepAfterChoices, settings.SleepAfterMinutes, BehaviorSettings.DefaultSleepAfterMinutes);
        KeepActiveDuringCalls = settings.KeepActiveDuringCalls;
        IsAutoConnectedEnabled = settings.IsAutoConnectEnabled;
        IsAutoConnectedToFallbackServersEnabled = settings.IsFallbackConnectionEnabled;
        IsFallbackConnectionWarningEnabled = settings.IsFallbackConnectionWarningEnabled;
        IsPropagatedConnectionWarningEnabled = settings.IsPropagatedConnectionWarningEnabled;
        CloseButtonActionIndex = (int)settings.CloseButtonAction;
        IsUpdateCheckOnStartupEnabled = settings.IsUpdateCheckOnStartupEnabled;
        UpdateChannelIndex = (int)settings.UpdateChannel;
        QuickReaction = settings.QuickReaction;
    }

    // The list clears its selection while it is rebuilt; nothing chosen keeps the default
    private static int ChoiceAt(IReadOnlyList<int> choices, int index, int fallback) =>
        index >= 0 && index < choices.Count ? choices[index] : fallback;

    // A value saved that the list does not offer shows the default
    private static int IndexOf(IReadOnlyList<int> choices, int value, int fallback)
    {
        var index = choices.ToList().IndexOf(value);
        return index >= 0 ? index : choices.ToList().IndexOf(fallback);
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
