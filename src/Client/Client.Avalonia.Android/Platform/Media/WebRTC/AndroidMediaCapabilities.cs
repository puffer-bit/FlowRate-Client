using System.Collections.Generic;
using Client.Application.Ports;

namespace Client.Droid.Platform.Media.WebRTC;

/// <summary>
/// Screens are not shared from Android, so there is no encoder to miss. The ones watched are decoded by
/// libwebrtc, which picks MediaCodec itself: there are no decoders to choose from.
/// </summary>
internal sealed class AndroidMediaCapabilities : IMediaCapabilities
{
    public bool HasHardwareScreenShareEncoder => true;

    public IReadOnlyList<string> ScreenShareDecoders => [];

    public IReadOnlyList<string> ScreenShareEncoders => [];

    public bool CanCaptureScreenAudio => false;
}
