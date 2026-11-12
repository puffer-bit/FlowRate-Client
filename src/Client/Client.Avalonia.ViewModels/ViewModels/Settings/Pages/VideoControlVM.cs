using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Threading;
using Client.Application.Ports;
using Client.Application.Settings;
using Client.Application.Settings.Search;
using Client.Avalonia.Localization;
using Client.Core.Media;
using Client.Core.Settings;
using Client.Services;
using Client.ViewModels.Dialogs;
using ReactiveUI;

using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Settings.Pages;

/// <summary>
/// How shared screens are decoded, shown and encoded.
/// </summary>
public class VideoControlVM : ReactiveObject, ISettingVM
{
    public string Name => SettingText.TabName("video");
    public SettingTabType Type => SettingTabType.Video;
    public bool IsEnabled => true;
    public bool IsMediaEnabled => MediaRuntime.IsEnabled;

    public IReadOnlyList<SettingSearchEntry> SearchEntries => SettingText.Entries("video",
        ("decoding", ["decoder", "av1Decoder", "vp8Decoder", "vp9Decoder", "zeroCopy"]),
        ("presentation", ["timedPresentation", "presentationBuffer"]),
        ("encoding", ["encoder", "av1Encoder", "vp8Encoder", "vp9Encoder"]));

    private VideoSettings _profiles = new();
    public int VideoTargetIndex
    {
        get;
        set
        {
            if (value is not (0 or 1) || field == value) return;
            _profiles = ToSettings();
            this.RaiseAndSetIfChanged(ref field, value);
            _isUpdating = true;
            Apply(_profiles);
            _isUpdating = false;
        }
    }

    public IReadOnlyList<CodecSelectionVM> Encoders { get; }
    public IReadOnlyList<CodecSelectionVM> Decoders { get; }

    public Interaction<MessageBoxVM, Result> ShowMessageBox { get; } = new();
    private bool _isCodecSettingsWarningAcknowledged;

    public async Task<CodecSettingsVM?> OpenCodecSettingsAsync(CodecSelectionVM selection)
    {
        if (!selection.CanConfigure)
            return null;
        if (!_isCodecSettingsWarningAcknowledged)
        {
            var answer = await ShowMessageBox.Handle(new MessageBoxVM(Icon.Warning, Buttons.OkCancel,
                Strings.Get("settings.video.codecSettings.warning"),
                Strings.Get("settings.video.codecSettings.title")));
            if (answer != Result.Ok)
                return null;
            try
            {
                await SaveSettingsAsync(() => ToSettings() with { IsCodecSettingsWarningAcknowledged = true });
                _isCodecSettingsWarningAcknowledged = true;
            }
            catch (Exception)
            {
                await ShowMessageBox.Handle(new MessageBoxVM(Icon.Error, Buttons.Ok,
                    Strings.Get("settings.video.codecSettings.saveFailed"),
                    Strings.Get("settings.video.codecSettings.title")));
                return null;
            }
        }
        return CreateCodecSettings(selection);
    }

    private void UpdateOverrideIndicators()
    {
        foreach (var selection in Encoders.Concat(Decoders))
            selection.UpdateOverrides(_codecOverrides);
    }

    public CodecSettingsVM? CreateCodecSettings(CodecSelectionVM selection)
    {
        if (!selection.CanConfigure || selection.EffectiveFactory is not { } factory)
            return null;
        return new CodecSettingsVM(factory, _mediaCapabilities.GetCodecParameters(factory), _codecOverrides,
            values => SaveOverridesAsync(factory, values));
    }

    private async Task SaveOverridesAsync(string factory, IReadOnlyList<CodecPropertyOverride> values)
    {
        await _saveLock.WaitAsync();
        try
        {
            var combined = _codecOverrides.Where(p => p.Factory != factory).Concat(values).ToArray();
            var current = ToSettings();
            var updated = VideoTargetIndex == 1
                ? current with { Camera = current.ForCamera() with { CodecOverrides = combined } }
                : current with { CodecOverrides = combined };
            await _videoService.SaveSettingsAsync(updated);
            _profiles = updated;
            _codecOverrides = combined;
            UpdateOverrideIndicators();
        }
        finally { _saveLock.Release(); }
    }

    public bool IsZeroCopyEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsTimedPresentationEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    // Index into PresentationBufferChoices
    public int PresentationBufferIndex
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>The ways without copying this computer has, or why there are none.</summary>
    public string ZeroCopyStatus
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = "";

    public IReadOnlyList<string> PresentationBufferChoices { get; } = VideoSettings.PresentationBufferChoices
        .Select(frames => frames.ToString(CultureInfo.InvariantCulture))
        .ToList();

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; init; }

    private readonly IVideoService _videoService;
    private readonly IMediaCapabilities _mediaCapabilities;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private IReadOnlyList<CodecPropertyOverride> _codecOverrides = [];

    private async Task SaveSettingsAsync(Func<VideoSettings> settings)
    {
        await _saveLock.WaitAsync();
        try { await _videoService.SaveSettingsAsync(settings()); }
        finally { _saveLock.Release(); }
    }

    // Set while the view model fills its own properties, so that only user changes get saved
    private bool _isUpdating = true;

    public VideoControlVM(IVideoService videoService, IMediaCapabilities mediaCapabilities)
    {
        _videoService = videoService;
        _mediaCapabilities = mediaCapabilities;

        var codecs = new[] { VideoCodec.H264, VideoCodec.AV1, VideoCodec.VP8, VideoCodec.VP9 };
        Encoders = codecs.Select(codec => CreateSelection(codec, true)).ToArray();
        Decoders = codecs.Select(codec => CreateSelection(codec, false)).ToArray();

        LoadCommand = ReactiveCommand.CreateFromTask(LoadAsync);

        ObserveSettingsChanges();
    }

    private CodecSelectionVM CreateSelection(VideoCodec codec, bool isEncoder)
    {
        var available = !IsMediaEnabled ? Array.Empty<string>() : isEncoder
            ? _mediaCapabilities.GetScreenShareEncoders(codec)
            : _mediaCapabilities.GetScreenShareDecoders(codec);
        return new CodecSelectionVM(codec, isEncoder, available, _mediaCapabilities.GetCodecParameters);
    }

    private void ObserveSettingsChanges()
    {
        var selections = Encoders.Concat(Decoders)
            .Select(selection => selection.WatchProperties(nameof(CodecSelectionVM.SelectedIndex)).Select(_ => 0));
        var presentation = this.WatchProperties(nameof(IsZeroCopyEnabled), nameof(IsTimedPresentationEnabled),
            nameof(PresentationBufferIndex)).Select(_ => 0);
        selections.Append(presentation).Merge()
            .Where(_ => !_isUpdating && IsMediaEnabled)
            .Select(_ => Observable.FromAsync(() => SaveSettingsAsync(ToSettings)))
            .Concat()
            .Subscribe();
    }

    // The automatic choice names the element it comes to: the first of the list, in order of preference
    internal static string ChoiceText(string? element, IReadOnlyList<string> available) => element == null
        ? available.Count == 0
            ? Strings.Get("settings.video.automatic")
            : Strings.Format("settings.video.automaticChoice", ("choice", ChoiceText(available[0], available)))
        : Strings.Format(VideoElementNames.IsSoftware(element) ? "settings.video.choice.cpu" : "settings.video.choice.gpu",
            ("name", VideoElementNames.Of(element)), ("element", element));

    // What this computer can show without copying, as the screen found out when the window opened
    private static string ZeroCopyStatusText()
    {
        var failed = VideoElementNames.Of(VideoSurfaceSupport.FailedKinds);
        var available = VideoElementNames.Of(VideoSurfaceSupport.Kinds);

        if (failed.Count > 0)
        {
            var ways = string.Join(", ", failed);

            return Strings.Format("settings.video.zeroCopyStatus.failed", ("ways",
                VideoSurfaceSupport.FailureReason is { } reason ? $"{ways} ({reason})" : ways));
        }

        if (available.Count > 0)
            return string.Join(", ", available);

        return Strings.Get(VideoSurfaceSupport.IsProbed
            ? "settings.video.zeroCopyStatus.unavailable"
            : "settings.video.zeroCopyStatus.unknown");
    }

    private async Task LoadAsync()
    {
        _isUpdating = true;
        ZeroCopyStatus = ZeroCopyStatusText();
        Apply(await _videoService.GetSettingsAsync());
        _isUpdating = false;
    }

    public async Task ResetAllAsync()
    {
        if (!IsMediaEnabled)
            return;

        var defaults = new VideoSettings { IsCodecSettingsWarningAcknowledged = _isCodecSettingsWarningAcknowledged };

        _isUpdating = true;
        Apply(defaults);
        _isUpdating = false;

        await SaveSettingsAsync(() => defaults);
    }

    private VideoSettings ToSettings()
    {
        var selected = ReadSelectedSettings();
        _profiles = VideoTargetIndex == 1
            ? _profiles with { Camera = selected, IsCodecSettingsWarningAcknowledged = _isCodecSettingsWarningAcknowledged,
                IsZeroCopyEnabled = selected.IsZeroCopyEnabled,
                IsTimedPresentationEnabled = selected.IsTimedPresentationEnabled,
                PresentationBufferFrames = selected.PresentationBufferFrames }
            : selected with { Camera = _profiles.Camera };
        return _profiles;
    }

    private VideoSettings ReadSelectedSettings() => new()
    {
        IsHardwareDecodingEnabled = true,
        CodecOverrides = _codecOverrides,
        IsCodecSettingsWarningAcknowledged = _isCodecSettingsWarningAcknowledged,
        Decoder = Decoder(VideoCodec.H264),
        Av1Decoder = Decoder(VideoCodec.AV1),
        Vp8Decoder = Decoder(VideoCodec.VP8),
        Vp9Decoder = Decoder(VideoCodec.VP9),
        Encoder = Encoder(VideoCodec.H264),
        Av1Encoder = Encoder(VideoCodec.AV1),
        Vp8Encoder = Encoder(VideoCodec.VP8),
        Vp9Encoder = Encoder(VideoCodec.VP9),
        IsZeroCopyEnabled = IsZeroCopyEnabled,
        IsTimedPresentationEnabled = IsTimedPresentationEnabled,
        PresentationBufferFrames = VideoSettings.PresentationBufferChoices[
            Math.Clamp(PresentationBufferIndex, 0, VideoSettings.PresentationBufferChoices.Count - 1)]
    };

    private void Apply(VideoSettings settings)
    {
        _profiles = settings;
        if (VideoTargetIndex == 1)
            settings = settings.ForCamera();
        _codecOverrides = settings.CodecOverrides;
        _isCodecSettingsWarningAcknowledged = _profiles.IsCodecSettingsWarningAcknowledged;
        UpdateOverrideIndicators();
        foreach (var encoder in Encoders)
            encoder.Load(settings.GetEncoder(encoder.Codec));
        foreach (var decoder in Decoders)
            decoder.Load(settings.GetDecoder(decoder.Codec), !settings.IsHardwareDecodingEnabled);
        IsZeroCopyEnabled = settings.IsZeroCopyEnabled;
        IsTimedPresentationEnabled = settings.IsTimedPresentationEnabled;
        PresentationBufferIndex = Math.Max(0,
            VideoSettings.PresentationBufferChoices.ToList().IndexOf(settings.PresentationBufferFrames));
    }

    private string? Encoder(VideoCodec codec) => Encoders.Single(selection => selection.Codec == codec).PreferredFactory;
    private string? Decoder(VideoCodec codec) => Decoders.Single(selection => selection.Codec == codec).PreferredFactory;
}
