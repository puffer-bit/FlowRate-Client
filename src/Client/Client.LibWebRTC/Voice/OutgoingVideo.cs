using Client.Application.Ports;
using Client.Core.Media;

namespace Client.LibWebRTC.Voice;

/// <summary>
/// A screen we send: its capture encodes the pictures, libwebrtc sends them, and asks the capture for
/// keyframes and for the bitrate the network takes.
/// </summary>
internal sealed class OutgoingVideo : IVideoSendListener, IDisposable
{
    // The encoder follows the estimate down to here. The encoder keeps to the bitrate itself and
    // libwebrtc drops none of its pictures, so whatever it puts out above the estimate waits in the
    // pacer: at 300, on a link that gave the screen 115 kbit/s, the pictures came seconds late
    private const int MinBitrateKbps = 100;

    // A smaller change of the estimate is left alone: each one the encoder takes costs it a moment
    private const double BitrateStep = 0.1;

    private readonly IScreenCapture _capture;
    private readonly Action<int> _onBitrateChanged;
    private int _bitrateKbps;

    /// <param name="onBitrateChanged">Told each bitrate the encoder is given, for the log.</param>
    public OutgoingVideo(IScreenCapture capture, ScreenShareSettings settings, Action<int> onBitrateChanged)
    {
        _capture = capture;
        _onBitrateChanged = onBitrateChanged;
        Settings = settings;
        _bitrateKbps = settings.EffectiveBitrateKbps;
    }

    public ScreenShareSettings Settings { get; }

    /// <summary>The least bitrate libwebrtc may go down to, in kbit/s.</summary>
    public int MinKbps => Math.Min(MinBitrateKbps, Settings.EffectiveBitrateKbps);

    /// <summary>What sends the pictures; set before the capture starts.</summary>
    public IVideoSendSource? Source { get; set; }

    // Never above what the user set: libwebrtc only goes up to it, but rounds on the way
    public void OnBitrate(int kbps)
    {
        var bitrate = Math.Clamp(kbps, MinKbps, Settings.EffectiveBitrateKbps);
        var current = Volatile.Read(ref _bitrateKbps);

        if (bitrate == current || (Math.Abs(bitrate - current) < current * BitrateStep &&
                                   bitrate != Settings.EffectiveBitrateKbps))
            return;

        Volatile.Write(ref _bitrateKbps, bitrate);
        _capture.SetBitrate(bitrate);
        _onBitrateChanged(bitrate);
    }

    public void OnKeyFrameRequested() => _capture.RequestKeyFrame();

    public void SetPreviewShown(bool isShown) => _capture.SetPreviewShown(isShown);

    /// <summary>Stops the capture, then the source it pushed into.</summary>
    public void Dispose()
    {
        _capture.Dispose();
        Source?.Dispose();
    }
}
