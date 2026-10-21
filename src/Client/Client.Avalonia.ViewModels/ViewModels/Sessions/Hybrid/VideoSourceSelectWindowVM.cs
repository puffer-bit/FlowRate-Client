using Client.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Client.Application.Ports;
using Client.Application.Ports.Repositories;
using Client.ViewModels.Dialogs;
using Result = Client.ViewModels.Dialogs.Result;
using Client.Avalonia.Localization;
using Client.Core.Media;
using Avalonia.Threading;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace Client.ViewModels.Sessions.Hybrid;

/// <summary>
/// The dialog that starts a screen share. Where the application can list the windows and monitors
/// itself, the user picks one of them here; where the system owns the choice, the dialog only calls
/// the system one and keeps what it returned.
/// </summary>
public class VideoSourceSelectWindowVM : ReactiveObject
{
    public ObservableCollection<ScreenSource> Windows { get; } = new();

    public ScreenSource? SelectedWindow
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ObservableCollection<ScreenSource> Monitors { get; } = new();

    public ScreenSource? SelectedMonitor
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// Monitors are being chosen from rather than windows.
    /// </summary>
    public bool IsMonitorsMode
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsWindowsMode));
            this.RaisePropertyChanged(nameof(AudioHint));
        }
    }

    public bool IsWindowsMode => !IsMonitorsMode;

    /// <summary>
    /// The sound is chosen on or off, and comes from what is shown: the window, or for a monitor
    /// the whole system without our own sound.
    /// </summary>
    public bool HasAudioToggle { get; }

    /// <summary>
    /// The sound is chosen by its application, apart from what is shown: the system dialog does not
    /// tell whose window was picked.
    /// </summary>
    public bool HasAudioApps { get; }

    public bool IsAudioEnabled
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsAudioDisabled));
            this.RaisePropertyChanged(nameof(AudioHint));
        }
    }

    public bool IsAudioDisabled => !IsAudioEnabled;

    /// <summary>
    /// Where the sound of the share comes from, or null while it is shared silent.
    /// </summary>
    public string? AudioHint =>
        !IsAudioEnabled ? null : Strings.Get(IsMonitorsMode ? "source.audio.monitor.hint" : "source.audio.window.hint");

    /// <summary>
    /// The applications that play sound, after the entry that shares none.
    /// </summary>
    public ObservableCollection<AudioAppOptionVM> AudioApps { get; } = new();

    public AudioAppOptionVM? SelectedAudioApp
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// No application plays sound, so the list holds nothing to choose.
    /// </summary>
    public bool HasNoAudioApps
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// The system shows its own dialog and decides what may be captured.
    /// </summary>
    public bool UsesSystemPicker => _screenCastClient != null;

    /// <summary>
    /// What the user picked in the system dialog.
    /// </summary>
    public VideoInput? SystemInput
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(HasSystemInput));
            this.RaisePropertyChanged(nameof(SystemInputSize));
        }
    }

    public bool HasSystemInput => SystemInput != null;

    /// <summary>
    /// The size of the picked picture, or null when the system did not report it.
    /// </summary>
    public string? SystemInputSize =>
        SystemInput is PipeWireVideoInput { Width: > 0, Height: > 0 } input ? $"{input.Width}×{input.Height}" : null;

    /// <summary>
    /// The system dialog is open.
    /// </summary>
    public bool IsPicking
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string? Error
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// The heights a large picture can be scaled down to.
    /// </summary>
    public IReadOnlyList<ScreenShareOptionVM> Resolutions { get; }

    public IReadOnlyList<ScreenShareOptionVM> Framerates { get; }

    /// <summary>
    /// The user sets the bitrate; otherwise it is counted from the resolution and the frame rate.
    /// </summary>
    public bool IsCustomBitrate
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsAutoBitrate));
            UpdateBitrate();
        }
    }

    public bool IsAutoBitrate => !IsCustomBitrate;

    /// <summary>
    /// The bitrate as the user typed it, in kbit/s.
    /// </summary>
    public string CustomBitrate
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            UpdateBitrate();
        }
    } = "";

    /// <summary>
    /// The automatic choice together with the bitrate it gives right now.
    /// </summary>
    public string AutoBitrateLabel
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = "";

    /// <summary>
    /// What is wrong with the bitrate the user set. It does not stop the share: a bitrate that is
    /// far from the recommended one is still used, and one that cannot be read gives way to the
    /// automatic one.
    /// </summary>
    public string? BitrateWarning
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// How the share is set up right now.
    /// </summary>
    public ScreenShareSettings Settings => new()
    {
        MaxHeight = Resolutions.First(option => option.IsSelected).Value,
        Framerate = Framerates.First(option => option.IsSelected).Value,
        BitrateKbps = IsCustomBitrate ? ParseCustomBitrate() : null,
        IsAudioEnabled = HasAudioApps ? SelectedAudioApp?.Key != null : IsAudioEnabled,
        AudioAppKey = HasAudioApps ? SelectedAudioApp?.Key : _savedAudioAppKey
    };

    /// <summary>
    /// Closes the dialog; the input is null when the user changed their mind.
    /// </summary>
    public Interaction<ScreenShareRequest?, RxVoid> CloseWindow { get; } = new();

    public Interaction<MessageBoxVM, Result> ShowMessageBox { get; } = new();

    public ReactiveCommand<RxVoid, RxVoid> SelectWindowsCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SelectMonitorsCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> EnableAudioCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DisableAudioCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SelectAutoBitrateCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SelectCustomBitrateCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> PickSystemSourceCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CloseCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> GoLive { get; }

    private readonly IScreenSourceProvider? _screenSourceProvider;
    private readonly IScreenCastClient? _screenCastClient;
    private readonly IDismissedPromptsRepository _dismissedPrompts;
    private readonly IAudioAppProvider? _audioApps;

    // Keeps the list of applications current while the dialog is open
    private IDisposable? _audioAppsWatch;

    // Kept for the next share where the application is not chosen here
    private readonly string? _savedAudioAppKey;

    private readonly AudioAppOptionVM _noAudioApp = new(null, Strings.Get("source.audio.off"));

    // Shown before the first share at a high frame rate, and never again
    private const string HighFrameratePromptKey = "screen.highFramerate";

    // The dialog is gone, so a source the system returns from now on has nobody to take it
    private bool _isAbandoned;

    /// <param name="audioApps">The applications the sound is taken from, where it is chosen by its application.</param>
    /// <param name="canShareAudio">The sound of a share can be captured on this system.</param>
    /// <param name="settings">The options the dialog opens with, as they were chosen the last time.</param>
    public VideoSourceSelectWindowVM(IScreenSourceProvider? screenSourceProvider, IScreenCastClient? screenCastClient,
        IAudioAppProvider? audioApps, bool canShareAudio, ScreenShareSettings settings,
        IDismissedPromptsRepository dismissedPrompts)
    {
        _dismissedPrompts = dismissedPrompts;
        _screenSourceProvider = screenSourceProvider;
        _screenCastClient = screenCastClient;

        settings = settings.Normalize();

        HasAudioToggle = canShareAudio && screenSourceProvider != null && screenCastClient == null;
        HasAudioApps = canShareAudio && audioApps != null && screenCastClient != null;
        IsAudioEnabled = settings.IsAudioEnabled;
        _savedAudioAppKey = settings.AudioAppKey;

        if (HasAudioApps)
        {
            _audioApps = audioApps;

            // Offered until the application plays again; until then it shows by its key
            SelectedAudioApp = settings is { IsAudioEnabled: true, AudioAppKey: { } key }
                ? new AudioAppOptionVM(key, key)
                : _noAudioApp;

            _audioApps!.Changed += OnAudioAppsChanged;
            _audioAppsWatch = _audioApps.Watch();
            UpdateAudioApps();
        }

        Resolutions = CreateOptions(ScreenShareSettings.MaxHeights, settings.MaxHeight,
            height => Strings.Format("source.resolution.value", ("height", height)), () => Resolutions!);
        Framerates = CreateOptions(ScreenShareSettings.Framerates, settings.Framerate,
            framerate => Strings.Format("source.framerate.value", ("framerate", framerate)), () => Framerates!,
            framerate => framerate > ScreenShareSettings.HighFramerateThreshold);

        CustomBitrate = (settings.BitrateKbps ?? settings.RecommendedBitrateKbps).ToString(CultureInfo.InvariantCulture);
        IsCustomBitrate = settings.BitrateKbps != null;

        EnableAudioCommand = ReactiveCommand.Create(() => { IsAudioEnabled = true; });
        DisableAudioCommand = ReactiveCommand.Create(() => { IsAudioEnabled = false; });
        SelectAutoBitrateCommand = ReactiveCommand.Create(() => { IsCustomBitrate = false; });
        SelectCustomBitrateCommand = ReactiveCommand.Create(() => { IsCustomBitrate = true; });
        SelectWindowsCommand = ReactiveCommand.Create(() => { IsMonitorsMode = false; });
        SelectMonitorsCommand = ReactiveCommand.Create(() => { IsMonitorsMode = true; });
        PickSystemSourceCommand = ReactiveCommand.CreateFromTask(PickSystemSourceAsync);
        CloseCommand = ReactiveCommand.CreateFromTask(async () => { await CloseWindow.Handle(null); });
        GoLive = ReactiveCommand.CreateFromTask(ConfirmAsync,
            Observable.Select(
                this.WatchProperties(nameof(SelectedWindow), nameof(SelectedMonitor), nameof(IsMonitorsMode),
                    nameof(SystemInput), nameof(IsPicking)),
                vm => !vm.IsPicking && (UsesSystemPicker
                    ? vm.SystemInput != null
                    : (vm.IsMonitorsMode ? vm.SelectedMonitor : vm.SelectedWindow) != null)));
    }

    /// <param name="group">The options made here, of which one is selected at a time.</param>
    private List<ScreenShareOptionVM> CreateOptions(IEnumerable<int> values, int selected,
        Func<int, string> label, Func<IReadOnlyList<ScreenShareOptionVM>> group, Func<int, bool>? isDemanding = null) =>
        values.Select(value => new ScreenShareOptionVM(value, label(value), option =>
        {
            foreach (var other in group())
                other.IsSelected = other == option;

            UpdateBitrate();
        }) { IsSelected = value == selected, IsDemanding = isDemanding?.Invoke(value) == true }).ToList();

    /// <returns>Null when what the user typed is not a bitrate that can be set.</returns>
    private int? ParseCustomBitrate() =>
        int.TryParse(CustomBitrate.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var bitrate) &&
        bitrate is > 0 and <= ScreenShareSettings.MaxBitrateKbps
            ? bitrate
            : null;

    /// <summary>
    /// Brings the recommended bitrate and the warning in line with the options.
    /// </summary>
    private void UpdateBitrate()
    {
        // Called while the options are still being made
        if (Resolutions == null || Framerates == null)
            return;

        var settings = Settings;
        var recommended = ("bitrate", (object?)settings.RecommendedBitrateKbps);

        AutoBitrateLabel = Strings.Format("source.bitrate.auto", recommended);

        if (!IsCustomBitrate)
            BitrateWarning = null;
        else if (settings.BitrateKbps is not { } bitrate)
            BitrateWarning = Strings.Format("source.bitrate.invalid", recommended);
        else if (settings.IsBitrateTooLow(bitrate))
            BitrateWarning = Strings.Format("source.bitrate.tooLow", recommended);
        else if (settings.IsBitrateTooHigh(bitrate))
            BitrateWarning = Strings.Format("source.bitrate.tooHigh", recommended);
        else
            BitrateWarning = null;
    }

    public void LoadSources()
    {
        if (_screenSourceProvider == null)
            return;

        Windows.Clear();
        Monitors.Clear();

        foreach (var window in _screenSourceProvider.GetWindows())
            Windows.Add(window);

        foreach (var monitor in _screenSourceProvider.GetMonitors())
            Monitors.Add(monitor);
    }

    // Raised on any thread
    private void OnAudioAppsChanged() => Dispatcher.UIThread.Post(UpdateAudioApps);

    /// <summary>
    /// Shows the applications that play sound now. The chosen one stays, even while it is silent:
    /// its sound is taken whenever it plays.
    /// </summary>
    private void UpdateAudioApps()
    {
        // A change posted before the dialog closed
        if (_audioApps == null || _audioAppsWatch == null)
            return;

        var selected = SelectedAudioApp;
        var apps = _audioApps.Apps.Select(app => new AudioAppOptionVM(app.Key, app.Name)).ToList();

        HasNoAudioApps = apps.Count == 0;

        if (selected?.Key != null && apps.FirstOrDefault(app => app.Key == selected.Key) is { } current)
            selected = current;
        else if (selected?.Key != null)
            apps.Insert(0, selected);

        AudioApps.Clear();
        AudioApps.Add(_noAudioApp);

        foreach (var app in apps)
            AudioApps.Add(app);

        SelectedAudioApp = selected ?? _noAudioApp;
    }

    private void StopWatchingAudioApps()
    {
        if (_audioApps == null || _audioAppsWatch == null)
            return;

        _audioApps.Changed -= OnAudioAppsChanged;
        _audioAppsWatch.Dispose();
        _audioAppsWatch = null;
    }

    /// <summary>
    /// The dialog ended without a share: gives a source that was picked back to the system.
    /// </summary>
    public void Abandon()
    {
        _isAbandoned = true;
        StopWatchingAudioApps();

        if (SystemInput == null)
            return;

        DropSystemInput();
        _ = _screenCastClient?.CloseAsync();
    }

    /// <summary>
    /// Forgets the picked source, which the system has closed or is about to replace.
    /// </summary>
    public void DropSystemInput()
    {
        if (SystemInput is PipeWireVideoInput pipeWire)
            pipeWire.Remote.Dispose();

        SystemInput = null;
    }

    private async Task PickSystemSourceAsync()
    {
        if (_screenCastClient == null)
            return;

        Error = null;
        IsPicking = true;

        // The system closes the earlier selection before it asks again
        DropSystemInput();

        try
        {
            SystemInput = await _screenCastClient.SelectSourceAsync();

            if (_isAbandoned)
                Abandon();
        }
        catch (ScreenCastException exception)
        {
            Error = Strings.Get(exception.Error == ScreenCastError.PortalUnavailable
                ? "hybrid.screen.portalUnavailable"
                : "hybrid.screen.portalFailed");
        }
        catch (ObjectDisposedException)
        {
            // The session was closed while the system dialog was open
            if (!_isAbandoned)
                await CloseWindow.Handle(null);
        }
        finally
        {
            IsPicking = false;
        }
    }

    private async Task ConfirmAsync()
    {
        VideoInput? input;

        if (UsesSystemPicker)
            input = SystemInput;
        else if (IsMonitorsMode)
            input = SelectedMonitor == null ? null : new MonitorVideoInput(SelectedMonitor.Handle);
        else
            input = SelectedWindow == null ? null : new WindowVideoInput(SelectedWindow.Handle);

        if (input == null)
            return;

        var settings = Settings;

        if (settings.IsHighFramerate && !await ConfirmHighFramerateAsync())
            return;

        var audioInput = CreateAudioInput();
        StopWatchingAudioApps();

        await CloseWindow.Handle(new ScreenShareRequest(input, settings, audioInput));
    }

    /// <returns>Null when the share goes silent.</returns>
    private AudioInput? CreateAudioInput()
    {
        if (HasAudioApps)
            return SelectedAudioApp?.Key is { } key ? new PipeWireAppAudioInput(key, _audioApps!) : null;

        if (!HasAudioToggle || !IsAudioEnabled)
            return null;

        var ownProcessId = (uint)Environment.ProcessId;

        // A monitor shares everything but our own sound, which carries the voices of the others.
        // Our own window has nothing else to share.
        if (IsMonitorsMode)
            return new ProcessLoopbackAudioInput(ownProcessId, IsExcluded: true);

        return SelectedWindow is { ProcessId: not 0 } window && window.ProcessId != ownProcessId
            ? new ProcessLoopbackAudioInput(window.ProcessId, IsExcluded: false)
            : null;
    }

    /// <summary>
    /// Warns once what a high frame rate asks of everyone; a refusal leaves the dialog open.
    /// </summary>
    private async Task<bool> ConfirmHighFramerateAsync()
    {
        if (await _dismissedPrompts.IsDismissedAsync(HighFrameratePromptKey))
            return true;

        var warning = new MessageBoxVM(Icon.Warning, Buttons.OkCancel,
            Strings.Get("source.highFramerate.message"), Strings.Get("source.highFramerate.title"));

        // Remembered as soon as it was seen, whatever the answer
        var result = await ShowMessageBox.Handle(warning);
        await _dismissedPrompts.SetAsync(HighFrameratePromptKey, true);

        return result == Result.Ok;
    }
}
