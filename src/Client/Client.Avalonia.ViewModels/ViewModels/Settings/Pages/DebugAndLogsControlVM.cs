using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Client.Application.Settings;
using Client.Application.Settings.Search;
using Client.Avalonia.Localization;
using Client.Core.Settings;
using Client.Services;
using Client.ViewModels.Dialogs;
using ReactiveUI;

using Result = Client.ViewModels.Dialogs.Result;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Settings.Pages;

/// <summary>
/// The journal of the application: how much it keeps, its files and an archive of them for a bug
/// report, the logs of GStreamer and libwebrtc in it; and the statistics of the media: what is drawn
/// over the screens and what of it goes into the journal.
/// </summary>
public class DebugAndLogsControlVM : ReactiveObject, ISettingVM
{
    // The choices of the journal in the order of the view, after the level of the build at index 0
    private static readonly JournalLevel[] JournalLevels =
        [JournalLevel.Verbose, JournalLevel.Debug, JournalLevel.Information, JournalLevel.Warning, JournalLevel.Error];

    private static readonly NativeLogLevel[] NativeLogLevels = Enum.GetValues<NativeLogLevel>();

    public string Name => SettingText.TabName("debug");
    public SettingTabType Type => SettingTabType.DebugAndLogs;
    public bool IsEnabled => true;

    public IReadOnlyList<SettingSearchEntry> SearchEntries => SettingText.Entries("debug",
        ("journal", ["level", "writeToFiles", "files", "archive"]),
        ("native", NativeSearchKeys()),
        ("mediaOverlay", ["mediaPerfOverlay", "mediaPerfExpanded", "mediaPerfSections", "mediaPerfInterval", "mediaPerfGraphs"]),
        ("mediaJournal", ["mediaPerfJournal", "mediaPerfSummary", "mediaPerfAnomalies"]));

    // The statistics of the media begin with what is drawn over the screens
    public IReadOnlyList<SettingAnchor> Anchors =>
    [
        new("debug.journal", "settings.debug.section.journal"),
        new("debug.native", "settings.debug.section.native"),
        new("debug.mediaOverlay", "settings.debug.anchors.media")
    ];

    // 0 is the level of the build, then JournalLevels
    public int JournalLevelIndex
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool WriteToFiles
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    // Indexes into NativeLogLevels, in the order of the enum
    public int GStreamerLogLevelIndex
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public int LibWebRTCLogLevelIndex
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    // The order of MediaPerfOverlayMode
    public int MediaPerfOverlayIndex
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsOverlayShown));
        }
    }

    public bool IsOverlayShown => MediaPerfOverlayIndex != (int)MediaPerfOverlayMode.Off;

    public bool IsMediaPerfExpanded
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool ShowNetworkSection
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool ShowTransportSection
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool ShowCodecSection
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool ShowRenderSection
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool ShowAudioSection
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    // Into DebugSettings.MediaPerfIntervals
    public int MediaPerfIntervalIndex
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool ShowMediaPerfGraphs
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    // The order of MediaPerfJournalMode
    public int MediaPerfJournalIndex
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsMediaJournalOn));
        }
    }

    public bool IsMediaJournalOn => MediaPerfJournalIndex != (int)MediaPerfJournalMode.Off;

    // Into DebugSettings.MediaPerfSummaryPeriods
    public int MediaPerfSummaryIndex
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool LogMediaPerfAnomalies
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>Whether this platform has GStreamer, whose log can be passed into the journal.</summary>
    public bool HasGStreamerLog { get; }

    public bool HasLibWebRTCLog { get; }

    /// <summary>Whether the journal is written to files here at all.</summary>
    public bool HasLogFiles => LogDirectory != null;

    public string? LogDirectory => _service.LogDirectory;

    /// <summary>What the files take.</summary>
    public string? LogFilesText
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>How the last archive went, or null before one.</summary>
    public string? ArchiveText
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> ClearLogFilesCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveArchiveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; init; }

    /// <summary>Asks the user before the files are deleted.</summary>
    public Interaction<MessageBoxVM, Result> ShowMessageBox { get; } = new();

    /// <summary>Asks where the archive goes, by its suggested name; null when the user gave up.</summary>
    public Interaction<string, Stream?> PickArchiveTarget { get; } = new();

    private readonly IDebugAndLogService _service;

    // Set while the view model fills its own properties, so that only user changes get saved
    private bool _isUpdating = true;

    public DebugAndLogsControlVM(IDebugAndLogService service)
    {
        _service = service;

        HasGStreamerLog = service.NativeLogSources.Contains(NativeLogSource.GStreamer);
        HasLibWebRTCLog = service.NativeLogSources.Contains(NativeLogSource.LibWebRTC);

        LoadCommand = ReactiveCommand.CreateFromTask(LoadAsync);
        ClearLogFilesCommand = ReactiveCommand.CreateFromTask(ClearLogFilesAsync);
        SaveArchiveCommand = ReactiveCommand.CreateFromTask(SaveArchiveAsync);

        ObserveSettingsChanges();
    }

    private void ObserveSettingsChanges()
    {
        // Every change saves the whole tab, one save at a time
        this.WatchProperties(
                nameof(JournalLevelIndex),
                nameof(WriteToFiles),
                nameof(GStreamerLogLevelIndex),
                nameof(LibWebRTCLogLevelIndex),
                nameof(MediaPerfOverlayIndex),
                nameof(IsMediaPerfExpanded),
                nameof(ShowNetworkSection),
                nameof(ShowTransportSection),
                nameof(ShowCodecSection),
                nameof(ShowRenderSection),
                nameof(ShowAudioSection),
                nameof(MediaPerfIntervalIndex),
                nameof(ShowMediaPerfGraphs),
                nameof(MediaPerfJournalIndex),
                nameof(MediaPerfSummaryIndex),
                nameof(LogMediaPerfAnomalies))
            .Where(_ => !_isUpdating)
            .Select(_ => Observable.FromAsync(() => _service.SaveSettingsAsync(ToSettings())))
            .Concat()
            .Subscribe();
    }

    private string[] NativeSearchKeys() =>
    [
        ..HasGStreamerLog ? ["gstreamer"] : Array.Empty<string>(),
        ..HasLibWebRTCLog ? ["libwebrtc"] : Array.Empty<string>()
    ];

    private async Task LoadAsync()
    {
        _isUpdating = true;
        Apply(await _service.GetSettingsAsync());
        _isUpdating = false;

        await RefreshFilesAsync();
    }

    private async Task RefreshFilesAsync()
    {
        if (!HasLogFiles)
            return;

        LogFilesText = Strings.Format("settings.debug.filesUsage",
            ("size", SizeText.Format(await _service.GetLogFilesSizeAsync())));
    }

    private async Task ClearLogFilesAsync()
    {
        var answer = await ShowMessageBox.Handle(new MessageBoxVM(Icon.Question, Buttons.YesNo,
            Strings.Get("settings.debug.files.question"), Strings.Get("settings.debug.files.title")));

        if (answer != Result.Yes)
            return;

        await _service.ClearLogFilesAsync();
        await RefreshFilesAsync();
    }

    private async Task SaveArchiveAsync()
    {
        var name = $"flowrate-logs-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.zip";
        var target = await PickArchiveTarget.Handle(name);

        if (target == null)
            return;

        try
        {
            await using (target)
                await _service.WriteArchiveAsync(target);

            ArchiveText = Strings.Format("settings.debug.archive.saved", ("name", name));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ArchiveText = Strings.Format("settings.debug.archive.failed", ("error", exception.Message));
        }
    }

    private DebugSettings ToSettings() => new()
    {
        JournalLevel = JournalLevelIndex <= 0 ? null : JournalLevels[Math.Min(JournalLevelIndex, JournalLevels.Length) - 1],
        WriteToFiles = WriteToFiles,
        GStreamerLogLevel = NativeLogLevels[Math.Clamp(GStreamerLogLevelIndex, 0, NativeLogLevels.Length - 1)],
        LibWebRTCLogLevel = NativeLogLevels[Math.Clamp(LibWebRTCLogLevelIndex, 0, NativeLogLevels.Length - 1)],
        MediaPerfOverlay = (MediaPerfOverlayMode)Math.Clamp(MediaPerfOverlayIndex, 0, 2),
        IsMediaPerfExpanded = IsMediaPerfExpanded,
        MediaPerfSections = (ShowNetworkSection ? MediaPerfSections.Network : 0) |
                            (ShowTransportSection ? MediaPerfSections.Transport : 0) |
                            (ShowCodecSection ? MediaPerfSections.Codec : 0) |
                            (ShowRenderSection ? MediaPerfSections.Render : 0) |
                            (ShowAudioSection ? MediaPerfSections.Audio : 0),
        MediaPerfIntervalMs = DebugSettings.MediaPerfIntervals[
            Math.Clamp(MediaPerfIntervalIndex, 0, DebugSettings.MediaPerfIntervals.Length - 1)],
        ShowMediaPerfGraphs = ShowMediaPerfGraphs,
        MediaPerfJournal = (MediaPerfJournalMode)Math.Clamp(MediaPerfJournalIndex, 0, 2),
        MediaPerfSummarySeconds = DebugSettings.MediaPerfSummaryPeriods[
            Math.Clamp(MediaPerfSummaryIndex, 0, DebugSettings.MediaPerfSummaryPeriods.Length - 1)],
        LogMediaPerfAnomalies = LogMediaPerfAnomalies
    };

    private void Apply(DebugSettings settings)
    {
        JournalLevelIndex = settings.JournalLevel is { } level ? Array.IndexOf(JournalLevels, level) + 1 : 0;
        WriteToFiles = settings.WriteToFiles;
        GStreamerLogLevelIndex = Math.Max(0, Array.IndexOf(NativeLogLevels, settings.GStreamerLogLevel));
        LibWebRTCLogLevelIndex = Math.Max(0, Array.IndexOf(NativeLogLevels, settings.LibWebRTCLogLevel));
        MediaPerfOverlayIndex = (int)settings.MediaPerfOverlay;
        IsMediaPerfExpanded = settings.IsMediaPerfExpanded;
        ShowNetworkSection = settings.MediaPerfSections.HasFlag(MediaPerfSections.Network);
        ShowTransportSection = settings.MediaPerfSections.HasFlag(MediaPerfSections.Transport);
        ShowCodecSection = settings.MediaPerfSections.HasFlag(MediaPerfSections.Codec);
        ShowRenderSection = settings.MediaPerfSections.HasFlag(MediaPerfSections.Render);
        ShowAudioSection = settings.MediaPerfSections.HasFlag(MediaPerfSections.Audio);
        MediaPerfIntervalIndex = Math.Max(0, Array.IndexOf(DebugSettings.MediaPerfIntervals, settings.MediaPerfIntervalMs));
        ShowMediaPerfGraphs = settings.ShowMediaPerfGraphs;
        MediaPerfJournalIndex = (int)settings.MediaPerfJournal;
        MediaPerfSummaryIndex = Math.Max(0, Array.IndexOf(DebugSettings.MediaPerfSummaryPeriods, settings.MediaPerfSummarySeconds));
        LogMediaPerfAnomalies = settings.LogMediaPerfAnomalies;
    }

    /// <summary>
    /// The defaults of the tab. The files of the journal are data and stay.
    /// </summary>
    public async Task ResetAllAsync()
    {
        var defaults = new DebugSettings();

        _isUpdating = true;
        Apply(defaults);
        _isUpdating = false;

        await _service.SaveSettingsAsync(defaults);
    }
}
