using System;
using System.IO;
using Avalonia.Platform;
using Client.Application.Sounds;

namespace Client.Services;

/// <summary>
/// The sounds that ship with the application, as resources of Client.Avalonia.Assets.
/// </summary>
public sealed class BuiltInSoundPack : ISoundPack
{
    private const string Folder = "avares://Client.Avalonia.Assets/Sounds/";

    public Stream? Open(SoundEvent soundEvent)
    {
        var uri = new Uri(Folder + SoundFiles.GetFileName(soundEvent));

        return AssetLoader.Exists(uri) ? AssetLoader.Open(uri) : null;
    }
}
