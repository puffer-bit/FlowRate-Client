using Client.Application.Coordinator.Users;
using Client.Application.Tests.Fakes;
using Client.Core.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Client.Application.Tests;

public class PeerPlaybackServiceTests
{
    private const string Instance = "instance";

    private readonly MemoryRememberedUsers _users = new();
    private readonly PeerPlaybackService _service;

    public PeerPlaybackServiceTests()
    {
        _service = new PeerPlaybackService(_users, NullLogger<PeerPlaybackService>.Instance);
    }

    [Fact]
    public async Task TheVoiceAndTheScreen_AreSetApart()
    {
        await _service.LoadAsync(Instance, [1]);
        List<(PeerAudioChannel Channel, PeerPlayback Playback)> changes = [];
        _service.Changed += (_, channel, playback) => changes.Add((channel, playback));

        _service.Set(1, PeerAudioChannel.Screen, new PeerPlayback(0.3, true, false));

        Assert.Equal(new PeerPlayback(0.3, true, false), _service.Get(1, PeerAudioChannel.Screen));
        Assert.Equal(PeerPlayback.Default, _service.Get(1, PeerAudioChannel.Voice));
        Assert.Equal([(PeerAudioChannel.Screen, new PeerPlayback(0.3, true, false))], changes);
    }

    [Fact]
    public async Task Load_ReadsBothChannels()
    {
        _users.Data[1] = new UserHybridSessionData
        {
            Volume = 50, IsMuted = true, ScreenVolume = 150, IsScreenVolumeOverrideEnabled = true
        };
        List<PeerAudioChannel> changed = [];
        _service.Changed += (_, channel, _) => changed.Add(channel);

        await _service.LoadAsync(Instance, [1]);

        Assert.Equal(new PeerPlayback(0.5, true, false), _service.Get(1, PeerAudioChannel.Voice));
        Assert.Equal(new PeerPlayback(1.5, false, true), _service.Get(1, PeerAudioChannel.Screen));
        Assert.Equal([PeerAudioChannel.Voice, PeerAudioChannel.Screen], changed);
    }

    [Fact]
    public async Task AChannelSetWhileLoading_WinsOverTheStoredOne_AndTheOtherIsStillLoaded()
    {
        _users.Data[1] = new UserHybridSessionData { Volume = 50, ScreenVolume = 70 };
        _users.LoadGate = new TaskCompletionSource();

        var loading = _service.LoadAsync(Instance, [1]);
        _service.Set(1, PeerAudioChannel.Screen, new PeerPlayback(0.2, false, false));
        _users.LoadGate.SetResult();
        await loading;

        Assert.Equal(0.2, _service.Get(1, PeerAudioChannel.Screen).Volume);
        Assert.Equal(0.5, _service.Get(1, PeerAudioChannel.Voice).Volume);
    }

    [Fact]
    public async Task Set_StoresBothChannels()
    {
        await _service.LoadAsync(Instance, [1]);

        _service.Set(1, PeerAudioChannel.Voice, new PeerPlayback(0.4, false, false));
        _service.Set(1, PeerAudioChannel.Screen, new PeerPlayback(0.8, true, false));

        for (var i = 0; i < 200 && !(_users.Data.TryGetValue(1, out var stored) && stored.IsScreenMuted); i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);

        var data = _users.Data[1];
        Assert.Equal((40, false, 80, true), (data.Volume, data.IsMuted, data.ScreenVolume, data.IsScreenMuted));
    }
}
