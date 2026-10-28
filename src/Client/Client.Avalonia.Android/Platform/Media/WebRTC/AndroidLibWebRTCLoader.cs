using Client.Application.Ports;

namespace Client.Droid.Platform.Media.WebRTC;

/// <summary>
/// libwebrtc is part of the application on Android and the only engine of the voice there.
/// </summary>
internal sealed class AndroidLibWebRTCLoader : ILibWebRTCLoader
{
    public string? Load() => null;
}
