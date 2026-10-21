using Client.Application.Ports;
using Client.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Client.Application.Sounds;

public sealed partial class SoundService : ISoundService
{
    private readonly ISoundPlayer _player;
    private readonly ISoundPack _builtInPack;
    private readonly ISoundPackCatalog _catalog;
    private readonly ILogger<SoundService> _logger;

    private readonly Lock _lock = new();
    // Decoded on the first play. A null value is a sound that could not be loaded: it is not tried again
    private readonly Dictionary<SoundEvent, SoundClip?> _clips = new();
    private readonly Dictionary<SoundEvent, SoundClip> _replacements = new();
    private ISoundPack? _pack;
    // The pack of the settings, as last applied
    private string? _packName;
    private float _volume = 1.0f;
    // Replaced as a whole, so that a play on another thread reads either the old set or the new one
    private HashSet<SoundEvent> _disabled = [];

    /// <param name="builtInPack">The sounds of the application, which have every event.</param>
    public SoundService(ISoundPlayer player, ISoundPack builtInPack, ISoundPackCatalog catalog,
        ILogger<SoundService> logger)
    {
        _player = player;
        _builtInPack = builtInPack;
        _catalog = catalog;
        _logger = logger;
    }

    public bool IsEnabled { get; set; } = true;

    public float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0.0f, 1.0f);
    }

    public void Apply(AudioSettings settings)
    {
        IsEnabled = settings.AreSoundsEnabled;
        Volume = (float)settings.SoundsVolume;

        var disabled = new HashSet<SoundEvent>();
        foreach (var name in settings.DisabledSounds)
        {
            if (SoundFiles.TryGetEvent(name, out var soundEvent))
                disabled.Add(soundEvent);
        }
        _disabled = disabled;

        lock (_lock)
        {
            if (settings.SoundPack == _packName)
                return;

            _packName = settings.SoundPack;
            // A pack that was removed leaves the built-in sounds
            UsePack(_packName == null ? null : _catalog.Open(_packName));
        }
    }

    public void Play(SoundEvent soundEvent)
    {
        if (IsEnabled && !_disabled.Contains(soundEvent))
            Preview(soundEvent);
    }

    public void Preview(SoundEvent soundEvent)
    {
        var volume = _volume;
        if (volume <= 0.0f)
            return;

        try
        {
            lock (_lock)
            {
                if (GetClip(soundEvent) is { } clip)
                    _player.Play(clip, volume);
            }
        }
        catch (Exception exception)
        {
            LogPlayFailed(exception, soundEvent);
        }
    }

    public void UsePack(ISoundPack? pack)
    {
        lock (_lock)
        {
            _pack = pack;

            foreach (var clip in _clips.Values)
                Release(clip);
            _clips.Clear();
        }
    }

    public bool Replace(SoundEvent soundEvent, Stream? file)
    {
        SoundClip? replacement = null;
        if (file != null)
        {
            try
            {
                replacement = WavReader.Read(file);
            }
            catch (Exception exception)
            {
                LogLoadFailed(exception, soundEvent);
                return false;
            }
        }

        lock (_lock)
        {
            if (_replacements.Remove(soundEvent, out var previous))
                Release(previous);

            if (replacement != null)
                _replacements[soundEvent] = replacement;
        }

        return true;
    }

    private SoundClip? GetClip(SoundEvent soundEvent)
    {
        if (_replacements.TryGetValue(soundEvent, out var replacement))
            return replacement;

        if (_clips.TryGetValue(soundEvent, out var clip))
            return clip;

        clip = (_pack != null ? Load(_pack, soundEvent) : null) ?? Load(_builtInPack, soundEvent);
        _clips[soundEvent] = clip;

        return clip;
    }

    private SoundClip? Load(ISoundPack pack, SoundEvent soundEvent)
    {
        try
        {
            using var stream = pack.Open(soundEvent);

            return stream == null ? null : WavReader.Read(stream);
        }
        catch (Exception exception)
        {
            LogLoadFailed(exception, soundEvent);
            return null;
        }
    }

    private void Release(SoundClip? clip)
    {
        if (clip == null)
            return;

        try
        {
            _player.Release(clip);
        }
        catch (Exception exception)
        {
            LogReleaseFailed(exception);
        }
    }
}
