using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using Avalonia.Threading;
using Client.Application.Coordinator.Users;
using Client.Application.Ports;
using Client.Application.Settings;
using Client.Core.Settings;
using Client.Core.Users;
using Client.Services;
using Client.ViewModels.Sessions.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Client.Application.Tests;

public sealed class ScreenAwakeTests
{
    [Fact]
    public Task MultipleSessionsShareOneInhibitionAndPreferenceChangesApplyImmediately() => TestUiThread.RunAsync(() =>
    {
        var behavior = new Behavior();
        using var service = new AwakeService(behavior);
        var first = service.Acquire();
        var second = service.Acquire();
        Assert.Equal([true], service.Transitions);
        first.Dispose();
        first.Dispose();
        Assert.Equal([true], service.Transitions);
        behavior.SaveSettingsAsync(new BehaviorSettings { KeepScreenOnWhileWatching = false }).GetAwaiter().GetResult();
        Assert.Equal([true, false], service.Transitions);
        behavior.SaveSettingsAsync(new BehaviorSettings()).GetAwaiter().GetResult();
        Assert.Equal([true, false, true], service.Transitions);
        second.Dispose();
        Assert.Equal([true, false, true, false], service.Transitions);
    });

    [Fact]
    public Task DisabledPreferenceDoesNotInhibitAndShutdownReleasesActiveLeases() => TestUiThread.RunAsync(() =>
    {
        var behavior = new Behavior { Settings = new() { KeepScreenOnWhileWatching = false } };
        var service = new AwakeService(behavior);
        var lease = service.Acquire();
        Assert.Empty(service.Transitions);
        behavior.SaveSettingsAsync(new BehaviorSettings()).GetAwaiter().GetResult();
        Assert.Equal([true], service.Transitions);
        service.Dispose();
        service.Dispose();
        lease.Dispose();
        behavior.SaveSettingsAsync(new BehaviorSettings()).GetAwaiter().GetResult();
        Assert.Equal([true, false], service.Transitions);
    });

    [Fact]
    public Task SavedPreferenceWinsOverPendingInitialLoad() => TestUiThread.RunAsync(() =>
    {
        var pending = new TaskCompletionSource<BehaviorSettings>();
        var behavior = new Behavior { Pending = pending.Task };
        using var service = new AwakeService(behavior);
        using var lease = service.Acquire();
        behavior.SaveSettingsAsync(new BehaviorSettings { KeepScreenOnWhileWatching = false }).GetAwaiter().GetResult();
        pending.SetResult(new BehaviorSettings());
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(service.Transitions);
    });

    [Fact]
    public Task WatchingLifetimeSurvivesFullscreenButReleasesOnDisconnectLeaveAndCollectionReset() => TestUiThread.RunAsync(() =>
    {
        var service = new LeaseService();
        var first = Stream(1);
        var second = Stream(2);
        ObservableCollection<ScreenStreamVM> streams = [first, second];
        using var session = new ScreenAwakeSession(streams, service);
        first.IsWatched = true;
        Assert.Equal(0, service.Active);
        session.SetConnected(true);
        Assert.Equal(1, service.Active);
        second.IsWatched = true;
        first.IsExpanded = true;
        first.IsExpanded = false;
        Assert.Equal(1, service.Acquisitions);
        first.IsWatched = false;
        Assert.Equal(1, service.Active);
        second.IsLeaving = true;
        Assert.Equal(0, service.Active);
        second.IsLeaving = false;
        Assert.Equal(1, service.Active);
        session.SetConnected(false);
        Assert.Equal(0, service.Active);
        session.SetConnected(true);
        Assert.Equal(1, service.Active);
        streams.Clear();
        Assert.Equal(0, service.Active);
        second.IsWatched = false;
        second.IsWatched = true;
        Assert.Equal(0, service.Active);
        streams.Add(second);
        Assert.Equal(1, service.Active);
        session.Dispose();
        Assert.Equal(0, service.Active);
        streams.Add(first);
        first.IsWatched = true;
        Assert.Equal(0, service.Active);
    });

    private static ScreenStreamVM Stream(int id) => new(id, false, $"User {id}", new Playback(), (_, _) => { }, _ => { });

    private sealed class AwakeService(Behavior behavior) : ScreenAwakeService(behavior, NullLogger.Instance)
    {
        public List<bool> Transitions { get; } = [];
        protected override void SetRequired(bool required) => Transitions.Add(required);
    }

    private sealed class LeaseService : IScreenAwakeService
    {
        public int Active { get; private set; }
        public int Acquisitions { get; private set; }
        public IDisposable Acquire()
        {
            Active++;
            Acquisitions++;
            return Disposable.Create(() => Active--);
        }
        public void Dispose() { }
    }

    private sealed class Behavior : IBehaviorService
    {
        public BehaviorSettings Settings { get; set; } = new();
        public Task<BehaviorSettings>? Pending { get; init; }
        public event Action<BehaviorSettings>? SettingsChanged;
        public Task<BehaviorSettings> GetSettingsAsync() => Pending ?? Task.FromResult(Settings);
        public Task SaveSettingsAsync(BehaviorSettings settings)
        {
            Settings = settings;
            SettingsChanged?.Invoke(settings);
            return Task.CompletedTask;
        }
        public Task<(SavedCoordinatorModel Coordinator, SavedUserModel User)?> GetAutoConnectInstance() => Task.FromResult<(SavedCoordinatorModel, SavedUserModel)?>(null);
        public Task SetLastIdentityAsync((string CoordinatorId, int UserId) identity) => Task.CompletedTask;
        public Task RemoveLastIdentityAsync() => Task.CompletedTask;
    }

    private sealed class Playback : IPeerPlaybackService
    {
        public event Action<int, PeerAudioChannel, PeerPlayback>? Changed { add { } remove { } }
        public Task LoadAsync(string coordinatorInstanceId, IReadOnlyCollection<int> userIds) => Task.CompletedTask;
        public PeerPlayback Get(int userId, PeerAudioChannel channel) => PeerPlayback.Default;
        public void Set(int userId, PeerAudioChannel channel, PeerPlayback playback) { }
    }
}
