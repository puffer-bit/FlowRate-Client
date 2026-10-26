using System.Collections.Generic;
using Client.Application.Ports;

namespace Client.Droid.Platform.Media.WebRTC;

/// <summary>
/// Screens are neither shared nor watched on Android, so there is no encoder to miss.
/// </summary>
internal sealed class AndroidMediaCapabilities : IMediaCapabilities
{
    public bool HasHardwareScreenShareEncoder => true;

    public IReadOnlyList<string> ScreenShareDecoders => [];

    public IReadOnlyList<string> ScreenShareEncoders => [];

    public bool CanCaptureScreenAudio => false;
}
