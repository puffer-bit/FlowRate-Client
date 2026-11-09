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
        ("decoding", ["decoder", "av1Decoder", "zeroCopy"]),
        ("presentation", ["timedPresentation", "presentationBuffer"]),
        ("encoding", ["encoder", "av1Encoder"]));

    // Index into DecoderChoices, the automatic choice first
    public int DecoderIndex
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    // Index into EncoderChoices, the automatic choice first
    public int EncoderIndex
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public int Av1DecoderIndex
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public int Av1EncoderIndex
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(CanConfigureAv1Encoder));
        }
    }

    private string? SelectedAv1Encoder => _av1Encoders[Math.Clamp(Av1EncoderIndex, 0, _av1Encoders.Count - 1)]
        ?? _av1Encoders.Skip(1).FirstOrDefault();

    public bool CanConfigureAv1Encoder => IsMediaEnabled && SelectedAv1Encoder is { } encoder
        && !VideoElementNames.IsSoftware(encoder);

    public EncoderSettingsVM? CreateEncoderSettings()
    {
        if (!CanConfigureAv1Encoder || SelectedAv1Encoder is not { } encoder)
            return null;
        return new EncoderSettingsVM(encoder, _mediaCapabilities.GetEncoderParameters(encoder), _encoderOverrides,
            async values =>
            {
                var combined = _encoderOverrides.Where(p => p.Encoder != encoder).Concat(values).ToArray();
                await SaveSettingsAsync(ToSettings() with { EncoderOverrides = combined });
                _encoderOverrides = combined;
            });
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

    public IReadOnlyList<string> DecoderChoices { get; }
    public IReadOnlyList<string> Av1DecoderChoices { get; }
    public IReadOnlyList<string> EncoderChoices { get; }
    public IReadOnlyList<string> Av1EncoderChoices { get; }

    public IReadOnlyList<string> PresentationBufferChoices { get; } = VideoSettings.PresentationBufferChoices
        .Select(frames => frames.ToString(CultureInfo.InvariantCulture))
        .ToList();

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; init; }

    private readonly IVideoService _videoService;
    private readonly IMediaCapabilities _mediaCapabilities;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private IReadOnlyList<EncoderPropertyOverride> _encoderOverrides = [];

    private async Task SaveSettingsAsync(VideoSettings settings)
    {
        await _saveLock.WaitAsync();
        try { await _videoService.SaveSettingsAsync(settings); }
        finally { _saveLock.Release(); }
    }

    // Element names behind DecoderChoices and EncoderChoices; null stands for the automatic choice
    private readonly IReadOnlyList<string?> _decoders;
    private readonly IReadOnlyList<string?> _av1Decoders;
    private readonly IReadOnlyList<string?> _encoders;
    private readonly IReadOnlyList<string?> _av1Encoders;

    // Set while the view model fills its own properties, so that only user changes get saved
    private bool _isUpdating = true;

    public VideoControlVM(IVideoService videoService, IMediaCapabilities mediaCapabilities)
    {
        _videoService = videoService;
        _mediaCapabilities = mediaCapabilities;

        IReadOnlyList<string> decoders = IsMediaEnabled ? mediaCapabilities.ScreenShareDecoders : [];
        IReadOnlyList<string> encoders = IsMediaEnabled ? mediaCapabilities.ScreenShareEncoders : [];
        _decoders = [null, ..decoders];
        IReadOnlyList<string> av1Decoders = IsMediaEnabled ? mediaCapabilities.ScreenShareAv1Decoders : [];
        _av1Decoders = [null, ..av1Decoders];
        Av1DecoderChoices = _av1Decoders.Select(element => ChoiceText(element, av1Decoders)).ToList();
        _encoders = [null, ..encoders];
        IReadOnlyList<string> av1Encoders = IsMediaEnabled ? mediaCapabilities.ScreenShareAv1Encoders : [];
        _av1Encoders = [null, ..av1Encoders];
        Av1EncoderChoices = _av1Encoders.Select(element => ChoiceText(element, av1Encoders)).ToList();
        DecoderChoices = _decoders.Select(element => ChoiceText(element, decoders)).ToList();
        EncoderChoices = _encoders.Select(element => ChoiceText(element, encoders)).ToList();

        LoadCommand = ReactiveCommand.CreateFromTask(LoadAsync);

        ObserveSettingsChanges();
    }

    private void ObserveSettingsChanges()
    {
        // Every change saves the whole tab, one save at a time
        this.WatchProperties(
                nameof(DecoderIndex),
                nameof(Av1DecoderIndex),
                nameof(EncoderIndex),
                nameof(Av1EncoderIndex),
                nameof(IsZeroCopyEnabled),
                nameof(IsTimedPresentationEnabled),
                nameof(PresentationBufferIndex))
            .Where(_ => !_isUpdating && IsMediaEnabled)
            .Select(_ => Observable.FromAsync(() => SaveSettingsAsync(ToSettings())))
            .Concat()
            .Subscribe();
    }

    // The automatic choice names the element it comes to: the first of the list, in order of preference
    private static string ChoiceText(string? element, IReadOnlyList<string> available) => element == null
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

        var defaults = new VideoSettings();

        _isUpdating = true;
        Apply(defaults);
        _isUpdating = false;

        await SaveSettingsAsync(defaults);
    }

    private VideoSettings ToSettings() => new()
    {
        IsHardwareDecodingEnabled = true,
        EncoderOverrides = _encoderOverrides,
        Decoder = _decoders[Math.Clamp(DecoderIndex, 0, _decoders.Count - 1)],
        Av1Decoder = _av1Decoders[Math.Clamp(Av1DecoderIndex, 0, _av1Decoders.Count - 1)],
        Encoder = _encoders[Math.Clamp(EncoderIndex, 0, _encoders.Count - 1)],
        Av1Encoder = _av1Encoders[Math.Clamp(Av1EncoderIndex, 0, _av1Encoders.Count - 1)],
        IsZeroCopyEnabled = IsZeroCopyEnabled,
        IsTimedPresentationEnabled = IsTimedPresentationEnabled,
        PresentationBufferFrames = VideoSettings.PresentationBufferChoices[
            Math.Clamp(PresentationBufferIndex, 0, VideoSettings.PresentationBufferChoices.Count - 1)]
    };

    private void Apply(VideoSettings settings)
    {
        _encoderOverrides = settings.EncoderOverrides;
        // An element that is not on this system anymore reads as the automatic choice
        // Preserve the legacy software-only preference through the decoder choice.
        var decoder = settings.IsHardwareDecodingEnabled
            ? settings.Decoder
            : _decoders.FirstOrDefault(element => element != null && VideoElementNames.IsSoftware(element));
        DecoderIndex = Math.Max(0, IndexOf(_decoders, decoder));
        var av1Decoder = settings.IsHardwareDecodingEnabled
            ? settings.Av1Decoder
            : _av1Decoders.FirstOrDefault(element => element != null && VideoElementNames.IsSoftware(element));
        Av1DecoderIndex = Math.Max(0, IndexOf(_av1Decoders, av1Decoder));
        EncoderIndex = Math.Max(0, IndexOf(_encoders, settings.Encoder));
        Av1EncoderIndex = Math.Max(0, IndexOf(_av1Encoders, settings.Av1Encoder));
        IsZeroCopyEnabled = settings.IsZeroCopyEnabled;
        IsTimedPresentationEnabled = settings.IsTimedPresentationEnabled;
        PresentationBufferIndex = Math.Max(0,
            VideoSettings.PresentationBufferChoices.ToList().IndexOf(settings.PresentationBufferFrames));
    }

    private static int IndexOf(IReadOnlyList<string?> choices, string? value)
    {
        for (var i = 0; i < choices.Count; i++)
        {
            if (choices[i] == value)
                return i;
        }

        return -1;
    }
}
