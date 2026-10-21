using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Client.Application.Ports;
using Client.Application.Settings;
using Client.Avalonia.Localization;
using Client.Services;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Main;

public enum UpdaterState
{
    /// <summary>Nothing has been asked yet.</summary>
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    /// <summary>Downloaded: a restart applies it, and so does the next start of the application.</summary>
    ReadyToRestart,
    Failed
}

/// <summary>
/// The updater menu of the top bar: looks for a newer release, downloads it and restarts the
/// application into it.
/// </summary>
public sealed partial class UpdaterVM : ReactiveObject
{
    public UpdaterState State
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(StatusText));
            this.RaisePropertyChanged(nameof(HasUpdate));
            this.RaisePropertyChanged(nameof(IsAvailable));
            this.RaisePropertyChanged(nameof(IsReadyToRestart));
            this.RaisePropertyChanged(nameof(IsDownloading));
            this.RaisePropertyChanged(nameof(IsBusy));
        }
    }

    /// <summary>
    /// Percents of the release downloaded.
    /// </summary>
    public int Progress
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(StatusText));
        }
    }

    /// <summary>
    /// The newer release found, or null.
    /// </summary>
    public AppUpdate? Update
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(DownloadText));
            this.RaisePropertyChanged(nameof(StatusText));
        }
    }

    /// <summary>
    /// Whether this copy can update itself; a build run from the sources cannot.
    /// </summary>
    public bool IsSupported => _updater.IsSupported;

    /// <summary>
    /// A newer release is there, downloaded or not: the menu button shows a mark.
    /// </summary>
    public bool HasUpdate => State is UpdaterState.Available or UpdaterState.ReadyToRestart
        || (State == UpdaterState.Downloading && Update != null);

    public bool IsAvailable => State == UpdaterState.Available;

    public bool IsReadyToRestart => State == UpdaterState.ReadyToRestart;

    public bool IsDownloading => State == UpdaterState.Downloading;

    public bool IsBusy => State == UpdaterState.Checking || IsDownloading;

    /// <summary>
    /// What the updater knows, the first line of its menu.
    /// </summary>
    public string StatusText => !IsSupported
        ? Strings.Get("main.updater.status.unsupported")
        : State switch
        {
            UpdaterState.Checking => Strings.Get("main.updater.status.checking"),
            UpdaterState.UpToDate => Strings.Format("main.updater.status.upToDate", ("version", CurrentVersion)),
            UpdaterState.Available => Strings.Format("main.updater.status.available", ("version", Update?.Version ?? "")),
            UpdaterState.Downloading => Strings.Format("main.updater.status.downloading", ("progress", Progress)),
            UpdaterState.ReadyToRestart => Strings.Format("main.updater.status.ready", ("version", Update?.Version ?? "")),
            UpdaterState.Failed => Strings.Get("main.updater.status.failed"),
            _ => Strings.Format("main.updater.status.current", ("version", CurrentVersion))
        };

    public string DownloadText => Update == null
        ? ""
        : Strings.Format("main.updater.download", ("version", Update.Version), ("size", SizeText.Format(Update.Size)));

    private string CurrentVersion => _updater.CurrentVersion ?? "";

    public ReactiveCommand<RxVoid, RxVoid> CheckCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DownloadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RestartCommand { get; }

    private readonly IAppUpdater _updater;
    private readonly IBehaviorService _behaviorService;
    private readonly INotificationService _notifications;
    private readonly ILogger<UpdaterVM> _logger;

    public UpdaterVM(IAppUpdater updater, IBehaviorService behaviorService, INotificationService notifications,
        ILogger<UpdaterVM> logger)
    {
        _updater = updater;
        _behaviorService = behaviorService;
        _notifications = notifications;
        _logger = logger;

        var isIdle = this.WatchProperty(nameof(IsBusy), vm => vm.IsBusy).Select(busy => !busy && IsSupported);
        CheckCommand = ReactiveCommand.CreateFromTask(() => CheckAsync(isQuiet: false), isIdle);
        DownloadCommand = ReactiveCommand.CreateFromTask(DownloadAsync,
            this.WatchProperty(nameof(IsAvailable), vm => vm.IsAvailable));
        RestartCommand = ReactiveCommand.Create(Restart,
            this.WatchProperty(nameof(IsReadyToRestart), vm => vm.IsReadyToRestart));
    }

    /// <summary>
    /// Looks for a newer release once the application has started, unless the user turned it off.
    /// Tells only about a release found: a failure goes to the log alone.
    /// </summary>
    public async Task CheckOnStartupAsync()
    {
        if (!IsSupported)
            return;

        try
        {
            if (!(await _behaviorService.GetSettingsAsync()).IsUpdateCheckOnStartupEnabled)
                return;
        }
        catch (Exception exception)
        {
            LogCheckFailed(exception);
            return;
        }

        await CheckAsync(isQuiet: true);
    }

    private async Task CheckAsync(bool isQuiet)
    {
        State = UpdaterState.Checking;

        try
        {
            // Read every time: the channel may have been changed in the settings since the last check
            var channel = (await _behaviorService.GetSettingsAsync()).UpdateChannel;
            Update = await _updater.CheckAsync(channel);
        }
        catch (Exception exception)
        {
            LogCheckFailed(exception);
            State = UpdaterState.Failed;
            if (!isQuiet)
                _notifications.Show(NotificationType.Error, Strings.Get("main.updater.checkFailed"));
            return;
        }

        if (Update == null)
        {
            State = UpdaterState.UpToDate;
            if (!isQuiet)
                _notifications.Show(NotificationType.Success, StatusText);
            return;
        }

        LogUpdateFound(Update.Version, _updater.CurrentVersion);
        State = UpdaterState.Available;
        _notifications.Show(NotificationType.Info, StatusText, Strings.Get("main.updater.availableHint"));
    }

    private async Task DownloadAsync()
    {
        if (Update is not { } update)
            return;

        Progress = 0;
        State = UpdaterState.Downloading;

        try
        {
            await _updater.DownloadAsync(update, percent => _ = UiThread.RunAsync(() =>
            {
                // A report may come after the download has ended
                if (State == UpdaterState.Downloading)
                    Progress = percent;
            }));
        }
        catch (Exception exception)
        {
            LogDownloadFailed(exception, update.Version);
            State = UpdaterState.Available;
            _notifications.Show(NotificationType.Error, Strings.Get("main.updater.downloadFailed"));
            return;
        }

        State = UpdaterState.ReadyToRestart;
        _notifications.Show(NotificationType.Success, StatusText, Strings.Get("main.updater.readyHint"));
    }

    private void Restart()
    {
        if (Update is not { } update)
            return;

        try
        {
            _updater.ApplyAndRestart(update);
        }
        catch (Exception exception)
        {
            LogApplyFailed(exception, update.Version);
            _notifications.Show(NotificationType.Error, Strings.Get("main.updater.applyFailed"));
        }
    }
}
