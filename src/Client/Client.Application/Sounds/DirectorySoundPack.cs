namespace Client.Application.Sounds;

/// <summary>
/// A pack that is a folder of files named as in <see cref="SoundFiles"/>.
/// </summary>
public sealed class DirectorySoundPack : ISoundPack
{
    private readonly string _directory;

    public DirectorySoundPack(string directory)
    {
        _directory = directory;
    }

    public Stream? Open(SoundEvent soundEvent)
    {
        var path = Path.Combine(_directory, SoundFiles.GetFileName(soundEvent));

        return File.Exists(path) ? File.OpenRead(path) : null;
    }
}
