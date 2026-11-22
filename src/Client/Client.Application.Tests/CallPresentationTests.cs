using Client.Application.Coordinator;
using Client.Core.Sessions;
using Client.Core.Users;
using Client.ViewModels.Sessions.Chat;
using Shared.Enums;
using Shared.Models.DataTransferObjects;
using Xunit;

namespace Client.Application.Tests;

public class CallPresentationTests
{
    private static ICoordinatorSession Coordinator(int currentUserId, Func<int, UserModel?> users) =>
        ChatHistoryWindowTests.Proxy<ICoordinatorSession>((method, args) => method switch
        {
            nameof(ICoordinatorSession.GetCurrentUser) => new UserModel
                { Id = currentUserId, Username = "me", CoordinatorInstanceId = "server" },
            nameof(ICoordinatorSession.GetUser) => Task.FromResult(users((int)args![0]!)),
            _ => throw new NotSupportedException(method)
        });

    private static UserModel User(int id, string? displayName) =>
        new() { Id = id, Username = "user" + id, DisplayName = displayName, CoordinatorInstanceId = "server" };

    private static ChatCallDTO Call(int callerId) => new(Guid.NewGuid(), 7, callerId, ChatCallState.Active, null,
        DateTimeOffset.UtcNow, null, [], false, null);

    private static ChatSessionModel Chat(ChatKind kind, params int[] members) =>
        new() { Id = 7, Name = "Friends", SessionType = SessionType.Chat, Kind = kind, MemberIds = members, CreatorUserId = 1, OwnerUserId = 1 };

    [Theory]
    [InlineData(2, "Bob", "Bob")]
    [InlineData(2, "  ", "user2")]
    public async Task DirectRing_ShowsTheCaller(int callerId, string displayName, string expected)
    {
        var coordinator = Coordinator(1, id => User(id, displayName));
        var (chat, caller) = await CallPresentation.GetNamesAsync(coordinator, Call(callerId), Chat(ChatKind.Direct, 1, 2));
        Assert.Equal(expected, chat);
        Assert.Equal(expected, caller);
    }

    [Fact]
    public async Task OwnDirectCall_ShowsTheOtherMember()
    {
        var coordinator = Coordinator(1, id => User(id, id == 2 ? "Bob" : "Me"));
        var (chat, _) = await CallPresentation.GetNamesAsync(coordinator, Call(1), Chat(ChatKind.Direct, 1, 2));
        Assert.Equal("Bob", chat);
    }

    [Fact]
    public async Task GroupRing_ShowsTheGroupAndTheCaller()
    {
        var coordinator = Coordinator(1, id => User(id, "Alice"));
        var (chat, caller) = await CallPresentation.GetNamesAsync(coordinator, Call(3), Chat(ChatKind.Group, 1, 2, 3));
        Assert.Equal("Friends", chat);
        Assert.Equal("Alice", caller);
    }

    [Fact]
    public async Task UnknownUser_FallsBackToAPlaceholder()
    {
        var coordinator = Coordinator(1, _ => null);
        var (chat, _) = await CallPresentation.GetNamesAsync(coordinator, Call(2), null);
        Assert.Equal(Client.Avalonia.Localization.Strings.Get("calls.peer"), chat);
    }
}
