namespace Client.Application.Sounds;

/// <summary>
/// The sound packs the user installed. A pack is a folder of <see cref="Directory"/> with files
/// named as in <see cref="SoundFiles"/>, and the name of the folder is the name of the pack.
/// </summary>
public interface ISoundPackCatalog
{
    string Directory { get; }

    /// <summary>
    /// Looks the packs up anew on every call.
    /// </summary>
    IReadOnlyList<string> GetPacks();

    /// <returns>Null when there is no such pack.</returns>
    ISoundPack? Open(string name);
}
