using Client.Services;
using System;
using System.Collections.Generic;
using Client.Application.Coordinator.Users;
using Client.Core.Media;
using Client.Core.Sessions.Hybrid;
using Client.Core.Users;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace Client.ViewModels.Sessions.Hybrid;

/// <summary>
/// The screen one user shows: a card that offers to watch it, or its picture once it is watched.
/// </summary>
public sealed class ScreenStreamVM : ReactiveObject
{
    public int UserId { get; }

    /// <summary>
    /// Our own screen, watched as a local preview.
    /// </summary>
    public bool IsSelf { get; }

    public string DisplayedName
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// The avatar of the user who shows the screen.
    /// </summary>
    public string? AvatarUrl
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// The screen is received and drawn.
    /// </summary>
    public bool IsWatched
    {
        get;
        set
        {
            if (field == value)
                return;

            this.RaiseAndSetIfChanged(ref field, value);

            // What was wrong with the stream before tells nothing about it once watched again
            _analyzer = null;
            Issues = [];
        }
    }

    /// <summary>
    /// What is wrong with the watched stream, one badge per area over the picture.
    /// </summary>
    public IReadOnlyList<StreamIssue> Issues
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = [];

    /// <summary>
    /// Reads the counters of the frames the picture got, or null while nothing draws it. Set by the
    /// view that draws the stream, which alone knows how many of them it dropped.
    /// </summary>
    public Func<FrameStats?>? ReadFrameStats { get; set; }

    /// <summary>
    /// The server was asked to start or stop the stream and has not answered yet.
    /// </summary>
    public bool IsBusy
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// The picture takes the whole surface and the other screens are hidden behind it.
    /// </summary>
    public bool IsExpanded
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// There is another screen to expand this one over. The only screen has the whole surface as it is.
    /// </summary>
    public bool CanExpand
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// Another screen is expanded.
    /// </summary>
    public bool IsHidden
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// The stream ended and its tile is playing its leave animation; it is removed from the list
    /// right after it.
    /// </summary>
    public bool IsLeaving
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    // Playback of the sound of the screen, applied locally only; the voice of the user is set apart
    public double Volume
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(VolumePercentText));
            this.RaisePropertyChanged(nameof(IsAmplified));
            PublishPlayback();
        }
    } = PeerPlayback.Default.Volume;

    // Linear gain shown as percent: 1.0 is 100%, overdrive goes up to 200%
    public string VolumePercentText => $"{Math.Round(Volume * 100)}%";

    // Above 100% the sound is amplified past the original
    public bool IsAmplified => Math.Round(Volume * 100) > 100;

    public bool IsMuted
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            PublishPlayback();
        }
    }

    public bool IsOverdriveEnabled
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(MaxVolume));

            if (!value && Volume > MaxVolume)
                Volume = MaxVolume;

            PublishPlayback();
        }
    }

    public double MaxVolume => IsOverdriveEnabled ? PeerPlayback.OverdriveMaxVolume : PeerPlayback.NormalMaxVolume;

    private readonly IPeerPlaybackService _playbackService;

    // Started with the first reading of the transport after watching starts
    private StreamHealthAnalyzer? _analyzer;

    // Set while the settings come from the service, so they are not sent back to it
    private bool _isApplyingPlayback;

    public ReactiveCommand<RxVoid, RxVoid> WatchCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> StopWatchingCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ToggleExpandCommand { get; }

    /// <summary>
    /// Raised for every frame while the screen is watched, on a thread of its track. The pixels are
    /// only valid during the call.
    /// </summary>
    public event Action<RawVideoFrame>? FrameReady;

    /// <summary>
    /// The stream ended or is no longer watched, so its last frame must not stay on the tile.
    /// </summary>
    public event Action? Cleared;

    /// <param name="setWatched">Starts or stops watching this screen.</param>
    /// <param name="toggleExpand">Expands this screen over the others, or brings them back.</param>
    public ScreenStreamVM(int userId, bool isSelf, string displayedName, IPeerPlaybackService playbackService,
        Action<ScreenStreamVM, bool> setWatched, Action<ScreenStreamVM> toggleExpand)
    {
        UserId = userId;
        IsSelf = isSelf;
        DisplayedName = displayedName;
        _playbackService = playbackService;

        ApplyPlayback(playbackService.Get(userId, PeerAudioChannel.Screen));

        var isIdle = this.WatchProperty(nameof(IsBusy), stream => !stream.IsBusy);

        WatchCommand = ReactiveCommand.Create(() => setWatched(this, true), isIdle);
        StopWatchingCommand = ReactiveCommand.Create(() => setWatched(this, false), isIdle);
        ToggleExpandCommand = ReactiveCommand.Create(() => toggleExpand(this),
            this.WatchProperty(nameof(CanExpand), stream => stream.CanExpand));
    }

    /// <summary>
    /// Shows settings of the sound of the screen loaded from the storage.
    /// </summary>
    public void ApplyPlayback(PeerPlayback playback)
    {
        _isApplyingPlayback = true;

        try
        {
            // The overdrive goes first, otherwise the slider would cut a louder volume down to its old maximum
            IsOverdriveEnabled = playback.IsOverdriveEnabled;
            Volume = playback.Volume;
            IsMuted = playback.IsMuted;
        }
        finally
        {
            _isApplyingPlayback = false;
        }
    }

    private void PublishPlayback()
    {
        if (_isApplyingPlayback || IsSelf)
            return;

        _playbackService.Set(UserId, PeerAudioChannel.Screen, new PeerPlayback(Volume, IsMuted, IsOverdriveEnabled));
    }

    public void ToggleMute() => IsMuted = !IsMuted;

    public void ToggleOverdrive() => IsOverdriveEnabled = !IsOverdriveEnabled;

    public void PushFrame(RawVideoFrame frame) => FrameReady?.Invoke(frame);

    /// <summary>
    /// Takes a reading of the transport statistics into what is wrong with the stream.
    /// </summary>
    /// <param name="isAudioExpected">The screen is watched with its sound.</param>
    /// <param name="now">The time of the reading, on a clock that only goes forward.</param>
    public void Analyze(TransportStatsSample sample, bool isAudioExpected, TimeSpan now)
    {
        if (!IsWatched || IsSelf)
            return;

        if (_analyzer == null)
        {
            _analyzer = new StreamHealthAnalyzer(UserId, now);
            return;
        }

        if (_analyzer.Update(sample, ReadFrameStats?.Invoke(), isAudioExpected, now))
            Issues = _analyzer.Issues;
    }

    public void Clear() => Cleared?.Invoke();
}
