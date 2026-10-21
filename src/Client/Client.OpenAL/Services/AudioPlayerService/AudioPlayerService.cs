using System.Collections.Concurrent;
using Silk.NET.OpenAL;
using Client.Application.Ports;


namespace Client.OpenAL.Services.AudioPlayerService;

public unsafe class AudioPlayerService : IAudioPlayerService
{
    private AL _al = null!;
    private ALContext _alc = null!; 
    private Device* _device;
    private Context* _context;
 
    private uint _uiSource;
    private readonly ConcurrentDictionary<string, uint> _userSources = new();

    /// <summary>
    /// The API of the opened device, or null while there is none: before <see cref="Init"/>, when
    /// it failed and after <see cref="Dispose"/>.
    /// </summary>
    internal AL? Api => _context != null ? _al : null;
    
    public void Init()
    {
        var library = new OpenALLibrary();
        _al = new AL(library);
        _alc = new ALContext(library);
        
        _device = _alc.OpenDevice(null);

        _context = _alc.CreateContext(_device, null);
        _alc.MakeContextCurrent(_context);

        _uiSource = _al.GenSource();
        _al.SetSourceProperty(_uiSource, SourceFloat.Gain, 0.4f);
    }

    public void AddChannel(string id)
    {
        if (_userSources.ContainsKey(id)) 
            return;

        var source = _al.GenSource();
        _al.SetSourceProperty(source, SourceFloat.Gain, 1.0f);
        _userSources.TryAdd(id, source);
    }

    public void RemoveChannel(string id)
    {
        _userSources.TryRemove(id, out var source);
        _al.SourceStop(source);
        _al.DeleteSource(source);
    }

    public void PlayPcm(byte[] pcmData)
    {
        PushPcmToSource(_uiSource, pcmData);
    }

    public void PlayPcm(string id, byte[] pcmData)
    {
        if (pcmData == null || pcmData.Length == 0) 
            return;

        if (_userSources.TryGetValue(id, out uint source))
        {
            PushPcmToSource(source, pcmData);
        }
    }

    public void ChangeVolume(float newVolume)
    {
        _al.SetSourceProperty(_uiSource, SourceFloat.Gain, newVolume);
    }

    public void ChangeVolume(string id, float newVolume)
    {
        if (_userSources.TryGetValue(id, out uint source))
        {
            _al.SetSourceProperty(source, SourceFloat.Gain, newVolume);
        }
    }

    public void Stop()
    {
        foreach (var source in _userSources.Values)
        {
            _al.SourceStop(source);
            _al.DeleteSource(source);
        }
        _userSources.Clear();
        
        _al.SourceStop(_uiSource);
        _al.DeleteSource(_uiSource);
    }
    
    private void PushPcmToSource(uint source, byte[] pcmData)
    {
        uint buffer = _al.GenBuffer();
        
        fixed (byte* ptr = pcmData)
        {
            _al.BufferData(buffer, BufferFormat.Stereo16, ptr, pcmData.Length, 44100);
        }
        
        _al.SourceQueueBuffers(source, 1, &buffer);

        _al.GetSourceProperty(source, GetSourceInteger.SourceState, out int state);
        if (state != (int)SourceState.Playing)
        {
            _al.SourcePlay(source);
        }

        CleanProcessedBuffers(source);
    }
    
    private void CleanProcessedBuffers(uint source)
    {
        _al.GetSourceProperty(source, GetSourceInteger.BuffersProcessed, out int processed);
        while (processed > 0)
        {
            uint unqueuedBuffer;
            _al.SourceUnqueueBuffers(source, 1, &unqueuedBuffer);
            _al.DeleteBuffer(unqueuedBuffer);
            processed--;
        }
    }
    
    public void Dispose()
    {
        Stop();
        if (_context != null)
        {
            _alc.DestroyContext(_context);
            _alc.CloseDevice(_device);
            _context = null;
        }
        GC.SuppressFinalize(this);
    }
}
