using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
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

    public IReadOnlyList<SettingSearchEntry> SearchEntries => SettingText.Entries("video",
        ("decoding", ["hardwareDecoding", "decoder", "zeroCopy"]),
        ("presentation", ["timedPresentation", "presentationBuffer"]),
        ("encoding", ["encoder"]));

    public bool IsHardwareDecodingEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

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
    public IReadOnlyList<string> EncoderChoices { get; }

    public IReadOnlyList<string> PresentationBufferChoices { get; } = VideoSettings.PresentationBufferChoices
        .Select(frames => frames.ToString(CultureInfo.InvariantCulture))
        .ToList();

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; init; }

    private readonly IVideoService _videoService;

    // Element names behind DecoderChoices and EncoderChoices; null stands for the automatic choice
    private readonly IReadOnlyList<string?> _decoders;
    private readonly IReadOnlyList<string?> _encoders;

    // Set while the view model fills its own properties, so that only user changes get saved
    private bool _isUpdating = true;

    public VideoControlVM(IVideoService videoService, IMediaCapabilities mediaCapabilities)
    {
        _videoService = videoService;

        _decoders = [null, ..mediaCapabilities.ScreenShareDecoders];
        _encoders = [null, ..mediaCapabilities.ScreenShareEncoders];
        DecoderChoices = _decoders.Select(element => ChoiceText(element, mediaCapabilities.ScreenShareDecoders)).ToList();
        EncoderChoices = _encoders.Select(element => ChoiceText(element, mediaCapabilities.ScreenShareEncoders)).ToList();

        LoadCommand = ReactiveCommand.CreateFromTask(LoadAsync);

        // Every change saves the whole tab, one save at a time
        this.WatchProperties(
                nameof(IsHardwareDecodingEnabled),
                nameof(DecoderIndex),
                nameof(EncoderIndex),
                nameof(IsZeroCopyEnabled),
                nameof(IsTimedPresentationEnabled),
                nameof(PresentationBufferIndex))
            .Where(_ => !_isUpdating)
            .Select(_ => Observable.FromAsync(() => _videoService.SaveSettingsAsync(ToSettings())))
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
        var defaults = new VideoSettings();

        _isUpdating = true;
        Apply(defaults);
        _isUpdating = false;

        await _videoService.SaveSettingsAsync(defaults);
    }

    private VideoSettings ToSettings() => new()
    {
        IsHardwareDecodingEnabled = IsHardwareDecodingEnabled,
        Decoder = _decoders[Math.Clamp(DecoderIndex, 0, _decoders.Count - 1)],
        Encoder = _encoders[Math.Clamp(EncoderIndex, 0, _encoders.Count - 1)],
        IsZeroCopyEnabled = IsZeroCopyEnabled,
        IsTimedPresentationEnabled = IsTimedPresentationEnabled,
        PresentationBufferFrames = VideoSettings.PresentationBufferChoices[
            Math.Clamp(PresentationBufferIndex, 0, VideoSettings.PresentationBufferChoices.Count - 1)]
    };

    private void Apply(VideoSettings settings)
    {
        IsHardwareDecodingEnabled = settings.IsHardwareDecodingEnabled;
        // An element that is not on this system anymore reads as the automatic choice
        DecoderIndex = Math.Max(0, IndexOf(_decoders, settings.Decoder));
        EncoderIndex = Math.Max(0, IndexOf(_encoders, settings.Encoder));
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
