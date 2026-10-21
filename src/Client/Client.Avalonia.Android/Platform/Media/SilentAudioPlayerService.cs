using Client.Application.Ports;

namespace Client.Droid.Platform.Media;

/// <summary>
/// Plays nothing: there is no audio output on Android yet, and no session that would use it.
/// </summary>
internal sealed class SilentAudioPlayerService : IAudioPlayerService
{
    public void Init()
    {
    }

    public void AddChannel(string id)
    {
    }

    public void RemoveChannel(string id)
    {
    }

    public void PlayPcm(byte[] pcmData)
    {
    }

    public void PlayPcm(string id, byte[] pcmData)
    {
    }

    public void ChangeVolume(float newVolume)
    {
    }

    public void ChangeVolume(string id, float newVolume)
    {
    }

    public void Stop()
    {
    }

    public void Dispose()
    {
    }
}
