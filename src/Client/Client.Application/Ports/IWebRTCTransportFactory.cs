namespace Client.Application.Ports;

public interface IWebRTCTransportFactory
{
    /// <param name="isPlaybackEnabled">Whether incoming audio is played on the output device.</param>
    IWebRTCTransport Create(int sessionId, bool isPlaybackEnabled = true);
}
