using Avalonia.Controls;
using Client.Services;
using Client.ViewModels.Sessions.Chat;
using Client.Views.Shared.Chat;
using Xunit;

namespace Client.Application.Tests;

public class ChatAudioPlayerTests
{
    [Fact]
    public Task PlatformSubclass_LoadsSharedMarkup_AndUpdatesSessionVolume() => TestUiThread.RunAsync(() =>
    {
        var session = new AudioSession();
        var player = new PlatformPlayer(session);

        Assert.NotNull(player.Content);
        var volume = player.FindControl<Slider>("VolumeSlider")!;
        Assert.Equal(25, volume.Value);
        Assert.Equal(0, session.VolumeChanges);

        volume.Value = 70;

        Assert.Equal(0.7, session.Volume);
        Assert.Equal(1, session.VolumeChanges);
    });

    private sealed class PlatformPlayer(IChatAudioSession session) : ChatAudioPlayer(session);

    private sealed class AudioSession : IChatAudioSession
    {
        public event Action? Changed { add { } remove { } }
        public double Volume { get; private set; } = 0.25;
        public int VolumeChanges { get; private set; }
        public bool IsLoading => false;
        public bool IsPlaying => false;
        public bool HasError => false;
        public double Position => 0;
        public double Duration => 0;
        public bool IsCurrent(ChatFileVM? file) => false;
        public Task InitializeAsync() => Task.CompletedTask;
        public Task ToggleAsync(ChatFileVM file) => Task.CompletedTask;
        public Task SetVolumeAsync(double volume)
        {
            Volume = volume;
            VolumeChanges++;
            return Task.CompletedTask;
        }
        public void Seek(ChatFileVM file, double position) { }
        public void Stop() { }
    }
}
