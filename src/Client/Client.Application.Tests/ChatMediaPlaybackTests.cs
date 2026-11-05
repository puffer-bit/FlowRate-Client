using System.Reflection;
using Avalonia.Threading;
using Client.Application.Coordinator;
using Client.Application.Ports;
using Client.Core.Chat;
using Client.Core.Coordinator;
using Client.Core.Users;
using Client.Services;
using Client.ViewModels.Sessions.Chat;
using Xunit;

namespace Client.Application.Tests;

public class ChatMediaPlaybackTests
{
    [Fact]
    public void StartingVideo_StopsAudio_AndReleasesItsCoordinatorSubscription()
    {
        using var playback = new ChatMediaPlayback();
        var coordinator = CreateCoordinator();
        var audio = new Media(playback);
        var video = new Media(playback);
        Assert.True(playback.Begin(audio, File(coordinator, 1, "audio.mp3")));

        var file = File(coordinator, 2, "video.mp4");
        Assert.True(playback.Begin(video, file));

        Assert.Equal(1, audio.Stops);
        Assert.Same(video, playback.Current);
        Assert.Same(file, playback.File);
        Assert.Single(coordinator.Handlers);
        playback.Stop();
        Assert.Equal(1, video.Stops);
        Assert.Null(playback.Current);
        Assert.Null(playback.File);
        Assert.Empty(coordinator.Handlers);
    }

    [Fact]
    public async Task Stop_PausesAndRewinds_WhileCloseReleasesPlayback()
    {
        using var playback = new ChatMediaPlayback();
        var media = new Media(playback) { IsPlaying = true, Position = 42 };
        var file = File(CreateCoordinator(), 1);
        playback.Begin(media, file);

        await playback.ResetAsync();

        Assert.False(media.IsPlaying);
        Assert.Equal(0, media.Position);
        Assert.Same(media, playback.Current);
        playback.Stop();
        Assert.Null(playback.Current);
        Assert.Equal(1, media.Stops);
    }

    [Fact]
    public async Task StopWhileLoading_CancelsTheSession()
    {
        using var playback = new ChatMediaPlayback();
        var media = new Media(playback) { IsLoading = true };
        playback.Begin(media, File(CreateCoordinator(), 1));

        await playback.ResetAsync();

        Assert.Equal(1, media.Stops);
        Assert.Null(playback.Current);
    }

    [Fact]
    public void DisconnectedCoordinator_CannotStartPlayback()
    {
        using var playback = new ChatMediaPlayback();
        var coordinator = CreateCoordinator();
        coordinator.State = CoordinatorState.Disconnected;

        Assert.False(playback.Begin(new Media(playback), File(coordinator, 1)));
        Assert.Null(playback.Current);
        Assert.Empty(coordinator.Handlers);
    }

    [Fact]
    public void Disconnect_StopsLoading_AndAnOldDisconnectCannotStopRestartedPlayback()
    {
        // The dispatcher belongs to a single dedicated thread for this test.
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                using var playback = new ChatMediaPlayback();
                var coordinator = CreateCoordinator();
                var file = File(coordinator, 1);
                var media = new Media(playback) { IsLoading = true };
                playback.Begin(media, file);
                coordinator.Handlers.Single()(CoordinatorState.Reconnecting);
                dispatcher.RunJobs();
                Assert.Equal(1, media.Stops);
                Assert.Null(playback.Current);

                playback.Begin(media, file);
                coordinator.Handlers.Single()(CoordinatorState.Disconnected);
                playback.Stop();
                playback.Begin(media, file);
                dispatcher.RunJobs();
                Assert.Same(media, playback.Current);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure != null)
            throw failure;
    }

    private static Coordinator CreateCoordinator() => (Coordinator)(object)DispatchProxy.Create<ICoordinatorSession, Coordinator>();

    private static ChatFileVM File(Coordinator coordinator, int id, string name = "audio.mp3") => new(
        new ChatFileContent { Id = id, FileName = name, Size = 1, MimeType = "application/octet-stream" },
        (ICoordinatorSession)(object)coordinator, _ => Task.FromResult<Stream?>(null),
        DispatchProxy.Create<IMediaCache, EmptyCache>(), _ => Task.FromResult(false));

    public class Coordinator : DispatchProxy
    {
        public CoordinatorState State = CoordinatorState.Connected;
        public List<Action<CoordinatorState>> Handlers { get; } = [];
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "get_ConnectionStatus": return State;
                case "GetCurrentUser": return new UserModel { Id = 1, Username = "user", CoordinatorInstanceId = "server" };
                case "add_StateChanged": Handlers.Add((Action<CoordinatorState>)args![0]!); return null;
                case "remove_StateChanged": Handlers.Remove((Action<CoordinatorState>)args![0]!); return null;
                default: throw new NotSupportedException(method.Name);
            }
        }
    }

    public class EmptyCache : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => null;
    }

    private sealed class Media(ChatMediaPlayback playback) : IChatMediaSession
    {
        public event Action? Changed;
        public double Volume => 1;
        public bool IsLoading { get; set; }
        public bool IsPlaying { get; set; }
        public bool HasError => false;
        public double Position { get; set; }
        public double Duration => 100;
        public int Stops { get; private set; }
        public Task ToggleAsync(ChatFileVM file) { IsPlaying = !IsPlaying; Changed?.Invoke(); return Task.CompletedTask; }
        public Task SetVolumeAsync(double volume) => Task.CompletedTask;
        public void Seek(ChatFileVM file, double position) { Position = position; Changed?.Invoke(); }
        public void Stop() { Stops++; IsPlaying = false; IsLoading = false; playback.End(this); Changed?.Invoke(); }
    }
}
