namespace Client.Application.Ports;

public interface IAudioPlayerService : IDisposable
{
    void Init();
    void AddChannel(string id);
    void RemoveChannel(string id);
    
    void PlayPcm(byte[] pcmData);
    void PlayPcm(string id, byte[] pcmData);
    
    void ChangeVolume(float newVolume);
    void ChangeVolume(string id, float newVolume);
    
    void Stop();
}