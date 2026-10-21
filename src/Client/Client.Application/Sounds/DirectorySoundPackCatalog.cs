namespace Client.Application.Sounds;

public sealed class DirectorySoundPackCatalog : ISoundPackCatalog
{
    public const string FolderName = "SoundPacks";

    public DirectorySoundPackCatalog(string directory)
    {
        Directory = directory;
    }

    public string Directory { get; }

    public IReadOnlyList<string> GetPacks()
    {
        try
        {
            // Created so that the user has a folder to put packs into
            System.IO.Directory.CreateDirectory(Directory);

            return System.IO.Directory.EnumerateDirectories(Directory)
                .Where(HasSounds)
                .Select(path => Path.GetFileName(path)!)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public ISoundPack? Open(string name)
    {
        // The name comes from the settings: only a folder right inside the catalog is a pack
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name is "." or "..")
            return null;

        var path = Path.Combine(Directory, name);

        return HasSounds(path) ? new DirectorySoundPack(path) : null;
    }

    private static bool HasSounds(string path)
    {
        try
        {
            return Enum.GetValues<SoundEvent>()
                .Any(soundEvent => File.Exists(Path.Combine(path, SoundFiles.GetFileName(soundEvent))));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
