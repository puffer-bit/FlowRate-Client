using System.Reflection;
using Client.Application.Chat;
using Client.Application.Coordinator;
using Client.Core.Chat;
using Client.Core.Coordinator;
using Client.ViewModels.Sessions.Chat;
using Shared.Enums;
using Shared.Models.Requests.SessionActions.ChatSession;
using Xunit;

namespace Client.Application.Tests;

public class ChatHistoryWindowTests
{
    private static ChatMessageModel Message(int id) => new()
    {
        Id = id, ChatId = 7, SenderUserId = 1, SentTime = DateTime.UtcNow,
        Images = [], Video = [], Files = [], Reactions = [], UsersIds = []
    };

    [Fact]
    public async Task HistoricalPagesLoadInBothDirectionsWithoutWritingGapsIntoTheTailCache()
    {
        var calls = new List<(int Id, ChatHistoryDirection Direction)>();
        var cacheCalls = new List<string>();
        var session = Proxy<ICoordinatorSession>((method, args) =>
        {
            if (method == nameof(ICoordinatorSession.RequestChatHistoryPage))
                return Task.FromResult(new RequestResult<ChatHistoryPage>(new ChatHistoryPage([Message(9000)], 100)));
            Assert.Equal(nameof(ICoordinatorSession.RequestChatHistoryWindow), method);
            var id = (int)args![1]!;
            var direction = (ChatHistoryDirection)args[2]!;
            calls.Add((id, direction));
            var messages = direction switch
            {
                ChatHistoryDirection.Around => new[] { Message(4999), Message(5000), Message(5001) },
                ChatHistoryDirection.Before => new[] { Message(4997), Message(4998) },
                _ => new[] { Message(5002), Message(5004) }
            };
            return Task.FromResult(new RequestResult<ChatHistoryWindow>(new(messages.ToList(), 100, true,
                direction != ChatHistoryDirection.After)));
        });
        var cache = Proxy<IChatCache>((method, _) => { cacheCalls.Add(method); return null; });
        var pager = new ChatHistoryPager(session, cache, 7);
        var around = await pager.LoadAroundAsync(5000);
        Assert.Equal(new[] { 4999, 5000, 5001 }, around!.Messages.Select(x => x.Id));
        Assert.True(pager.HasNewer);
        var older = await pager.LoadOlderAsync(4999);
        Assert.Equal(new[] { 4997, 4998 }, older!.Messages.Select(x => x.Id));
        var newer = await pager.LoadNewerAsync(5001);
        Assert.Equal(new[] { 5002, 5004 }, newer!.Messages.Select(x => x.Id));
        Assert.False(pager.HasNewer);
        Assert.Null(await pager.LoadNewerAsync(5004));
        Assert.Equal(new[] { (5000, ChatHistoryDirection.Around), (4999, ChatHistoryDirection.Before),
            (5001, ChatHistoryDirection.After) }, calls);
        Assert.Empty(cacheCalls);

        pager.Reset();
        var latest = await pager.LoadOlderAsync(null);
        Assert.Equal(9000, Assert.Single(latest!.Messages).Id);
        Assert.Equal(new[] { nameof(IChatCache.Replace) }, cacheCalls);
        Assert.False(pager.IsHistoricalWindow);
    }

    [Fact]
    public async Task FailedOrConcurrentNavigationDoesNotChangeTheCurrentWindow()
    {
        var pending = new TaskCompletionSource<RequestResult<ChatHistoryWindow>>();
        var session = Proxy<ICoordinatorSession>((_, _) => pending.Task);
        var cache = Proxy<IChatCache>((_, _) => throw new InvalidOperationException("Unexpected cache access"));
        var pager = new ChatHistoryPager(session, cache, 7);
        var loading = pager.LoadAroundAsync(5);
        Assert.True(pager.IsBusy);
        Assert.Null(await pager.LoadAroundAsync(6));
        pending.SetResult(ErrorCode.MessageNotFound);
        Assert.Null(await loading);
        Assert.False(pager.IsBusy);
        Assert.False(pager.IsHistoricalWindow);
        Assert.False(pager.HasNewer);
    }

    internal static T Proxy<T>(Func<string, object?[]?, object?> call) where T : class
    {
        var result = DispatchProxy.Create<T, Stub>();
        ((Stub)(object)result).Call = call;
        return result;
    }
    public class Stub : DispatchProxy
    {
        public Func<string, object?[]?, object?> Call { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!.Name, args);
    }
}
