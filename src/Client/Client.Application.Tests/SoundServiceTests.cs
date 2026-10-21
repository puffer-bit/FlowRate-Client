using Client.Application.Ports;
using Client.Application.Sounds;
using Client.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Client.Application.Tests;

public class SoundServiceTests
{
    private sealed class Player : ISoundPlayer
    {
        public List<SoundClip> Played { get; } = [];
        public List<SoundClip> Released { get; } = [];

        public void Play(SoundClip clip, float volume) => Played.Add(clip);

        public void Release(SoundClip clip) => Released.Add(clip);
    }

    // Every sound of a pack is one frame whose first byte tells the packs apart
    private sealed class Pack(byte mark, params SoundEvent[] events) : ISoundPack
    {
        public int Opened { get; private set; }

        public Stream? Open(SoundEvent soundEvent)
        {
            Opened++;

            return events.Contains(soundEvent) ? new MemoryStream(Wav([mark, 0, 0, 0])) : null;
        }
    }

    private static byte[] Wav(byte[] samples, ushort channels = 2, ushort bits = 16, ushort format = 1)
    {
        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8);
        writer.Write(36 + samples.Length);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write(format);
        writer.Write(channels);
        writer.Write(48000);
        writer.Write(48000 * channels * bits / 8);
        writer.Write((ushort)(channels * bits / 8));
        writer.Write(bits);
        writer.Write("data"u8);
        writer.Write(samples.Length);
        writer.Write(samples);

        return stream.ToArray();
    }

    private sealed class Catalog : ISoundPackCatalog
    {
        public string Directory => "";

        public IReadOnlyList<string> GetPacks() => ["Retro"];

        public ISoundPack? Open(string name) => name == "Retro" ? new Pack(2, SoundEvent.Join) : null;
    }

    private static readonly SoundEvent[] AllEvents = Enum.GetValues<SoundEvent>();

    private static (SoundService Service, Player Player, Pack BuiltIn) Create()
    {
        var player = new Player();
        var builtIn = new Pack(1, AllEvents);

        return (new SoundService(player, builtIn, new Catalog(), NullLogger<SoundService>.Instance), player, builtIn);
    }

    [Fact]
    public void WavIsRead()
    {
        var clip = WavReader.Read(new MemoryStream(Wav([1, 2, 3, 4, 5, 6, 7, 8, 9])));

        Assert.Equal(2, clip.Channels);
        Assert.Equal(48000, clip.SampleRate);
        // The incomplete frame at the end is dropped
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], clip.Pcm);
    }

    [Fact]
    public void UnsupportedWavIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => WavReader.Read(new MemoryStream(Wav([0, 0, 0], bits: 24))));
        Assert.Throws<InvalidDataException>(() => WavReader.Read(new MemoryStream(Wav([0, 0, 0, 0], format: 3))));
        Assert.Throws<InvalidDataException>(() => WavReader.Read(new MemoryStream([1, 2, 3])));
    }

    [Fact]
    public void EveryEventHasItsOwnFileName()
    {
        Assert.Equal(AllEvents.Length, AllEvents.Select(SoundFiles.GetFileName).Distinct().Count());
    }

    [Fact]
    public void SoundIsLoadedOnce()
    {
        var (service, player, builtIn) = Create();

        service.Play(SoundEvent.Join);
        service.Play(SoundEvent.Join);

        Assert.Equal(1, builtIn.Opened);
        Assert.Equal(2, player.Played.Count);
        Assert.Same(player.Played[0], player.Played[1]);
    }

    [Fact]
    public void DisabledOrSilentPlaysNothing()
    {
        var (service, player, _) = Create();

        service.IsEnabled = false;
        service.Play(SoundEvent.Join);
        service.IsEnabled = true;
        service.Volume = 0;
        service.Play(SoundEvent.Join);

        Assert.Empty(player.Played);
    }

    [Fact]
    public void PackReplacesItsEventsAndBuiltInCoversTheRest()
    {
        var (service, player, _) = Create();

        service.Play(SoundEvent.Join);
        service.UsePack(new Pack(2, SoundEvent.Join));
        service.Play(SoundEvent.Join);
        service.Play(SoundEvent.Leave);

        Assert.Equal([1, 2, 1], player.Played.Select(clip => clip.Pcm[0]));
        Assert.Equal([player.Played[0]], player.Released);
    }

    [Fact]
    public void SettingsChooseThePackAndARemovedOneLeavesTheBuiltInSounds()
    {
        var (service, player, _) = Create();

        service.Apply(new AudioSettings { SoundPack = "Retro", SoundsVolume = 0.5 });
        service.Play(SoundEvent.Join);
        service.Apply(new AudioSettings { SoundPack = "Gone" });
        service.Play(SoundEvent.Join);
        service.Apply(new AudioSettings { AreSoundsEnabled = false });
        service.Play(SoundEvent.Join);

        Assert.Equal([2, 1], player.Played.Select(clip => clip.Pcm[0]));
    }

    [Fact]
    public void CatalogListsOnlyFoldersWithSoundsAndOpensOnlyItsOwn()
    {
        var root = System.IO.Directory.CreateTempSubdirectory("sound-packs-").FullName;

        try
        {
            System.IO.Directory.CreateDirectory(Path.Combine(root, "Empty"));
            System.IO.Directory.CreateDirectory(Path.Combine(root, "Retro"));
            File.WriteAllBytes(Path.Combine(root, "Retro", "join.wav"), Wav([5, 0, 0, 0]));

            var catalog = new DirectorySoundPackCatalog(root);

            Assert.Equal(["Retro"], catalog.GetPacks());
            Assert.Null(catalog.Open("Empty"));
            Assert.Null(catalog.Open(".."));
            Assert.Null(catalog.Open(Path.Combine("..", Path.GetFileName(root), "Retro")));

            var pack = catalog.Open("Retro")!;
            Assert.Null(pack.Open(SoundEvent.Leave));
            using var join = pack.Open(SoundEvent.Join)!;
            Assert.Equal(5, WavReader.Read(join).Pcm[0]);
        }
        finally
        {
            System.IO.Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SwitchedOffSoundIsSilentButCanStillBeListenedTo()
    {
        var (service, player, _) = Create();

        service.Apply(new AudioSettings { DisabledSounds = ["join", "no_such_sound"] });
        service.Play(SoundEvent.Join);
        service.Play(SoundEvent.Leave);
        Assert.Single(player.Played);

        service.Preview(SoundEvent.Join);
        service.Apply(new AudioSettings { AreSoundsEnabled = false });
        service.Preview(SoundEvent.Join);
        Assert.Equal(3, player.Played.Count);

        service.Apply(new AudioSettings());
        service.Play(SoundEvent.Join);
        Assert.Equal(4, player.Played.Count);
    }

    [Fact]
    public void EventIsFoundByItsFileName()
    {
        Assert.All(AllEvents, soundEvent =>
        {
            Assert.True(SoundFiles.TryGetEvent(SoundFiles.GetName(soundEvent), out var found));
            Assert.Equal(soundEvent, found);
        });
        Assert.False(SoundFiles.TryGetEvent("no_such_sound", out _));
    }

    [Fact]
    public void SingleSoundIsReplacedOverThePackAndRestored()
    {
        var (service, player, _) = Create();

        Assert.True(service.Replace(SoundEvent.Join, new MemoryStream(Wav([3, 0, 0, 0]))));
        service.Play(SoundEvent.Join);
        Assert.False(service.Replace(SoundEvent.Join, new MemoryStream([1, 2, 3])));
        service.Play(SoundEvent.Join);
        Assert.True(service.Replace(SoundEvent.Join, null));
        service.Play(SoundEvent.Join);

        Assert.Equal([3, 3, 1], player.Played.Select(clip => clip.Pcm[0]));
    }
}
