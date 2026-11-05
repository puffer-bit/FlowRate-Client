using System.Reflection;
using Client.Application.Chat;
using Client.Application.Coordinator;
using Client.Application.Ports.Repositories;
using Client.Core.Coordinator;
using Client.Core.Sessions;
using Client.Core.Users;
using Client.ViewModels.Sessions;
using Client.ViewModels.Sessions.Chat;
using ReactiveUI;
using Shared.Enums;
using Xunit;

namespace Client.Application.Tests;

public sealed class ChatOpeningTests
{
    [Fact]
    public void ConstructionAndDisposalDoNotStartHistoryLoading()
    {
        using var command = ReactiveCommand.Create<int>(_ => { });
        var cache = Proxy<IChatCache>(_ => throw new InvalidOperationException("History read before the chat was displayed"));
        var chat = Create(cache, command);

        Assert.True(chat.IsInitialLoading);
        Assert.Empty(chat.Messages);
        chat.Dispose();
        Assert.True(chat.EnsureLoadedAsync().IsCompletedSuccessfully);
    }

    [Fact]
    public async Task LoadingDoesNotBlockCallerAndRepeatedOpeningSharesTheSameLoad()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var returned = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var command = ReactiveCommand.Create<int>(_ => { });
        var reads = 0;
        var cache = Proxy<IChatCache>(method =>
        {
            Assert.Equal(nameof(IChatCache.LoadNewestAsync), method);
            Interlocked.Increment(ref reads);
            entered.TrySetResult();
            release.Wait();
            return Task.FromResult<CachedChatPage?>(new([], 0, true));
        });
        var chat = Create(cache, command);
        // A dedicated caller stands in for the UI, so a regression cannot hang the test runner.
        var caller = new Thread(() => returned.SetResult(chat.EnsureLoadedAsync())) { IsBackground = true };
        caller.Start();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var load = await returned.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(load.IsCompleted);
            Assert.Same(load, chat.EnsureLoadedAsync());
            Assert.Equal(1, Volatile.Read(ref reads));
        }
        finally
        {
            // A chat closed during the fetch must discard its result without updating the UI.
            chat.Dispose();
            release.Set();
            caller.Join(TimeSpan.FromSeconds(10));
            await (await returned.Task).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        Assert.Empty(chat.Messages);
    }

    private static ActiveChatSessionVM Create(IChatCache cache,
        ReactiveCommand<int, ReactiveUI.Primitives.RxVoid> command)
    {
        var session = Proxy<ICoordinatorSession>(method => method switch
        {
            nameof(ICoordinatorSession.GetCurrentUser) => new UserModel
                { Id = 1, Username = "user", CoordinatorInstanceId = "coordinator" },
            "get_Permissions" => CoordinatorPermissions.None,
            "get_ConnectionStatus" => CoordinatorState.Connected,
            nameof(ICoordinatorSession.GetChatTypingUsers) => new Dictionary<int, DateTimeOffset>(),
            _ when method.StartsWith("add_") || method.StartsWith("remove_") => null,
            _ => throw new InvalidOperationException(method)
        });
        var caches = Proxy<IChatCacheStore>(method => method == nameof(IChatCacheStore.Open)
            ? cache : throw new InvalidOperationException(method));
        var services = new ChatSessionServices(caches, null!, null!, null!, null!);
        return new ActiveChatSessionVM(new ChatSessionModel
        {
            Id = 1, Name = "chat", CreatorUserId = 1, Capacity = null, SessionType = SessionType.Chat
        }, session, services, new ChatReadTracker(TimeSpan.Zero), command);
    }

    private static T Proxy<T>(Func<string, object?> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, Stub>();
        ((Stub)(object)proxy).Call = call;
        return proxy;
    }

    public class Stub : DispatchProxy
    {
        public Func<string, object?> Call { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!.Name);
    }
}
