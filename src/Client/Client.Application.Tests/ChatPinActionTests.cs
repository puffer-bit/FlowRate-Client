using Client.Core.Chat;
using Client.ViewModels.Sessions.Chat;
using ReactiveUI;
using Xunit;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.Application.Tests;

public class ChatPinActionTests
{
    private static ChatMessageModel Message(bool pinned = false) => new()
    {
        Id = 1, ChatId = 7, SenderUserId = 2, SentTime = DateTime.UtcNow,
        PinnedAt = pinned ? DateTime.UtcNow : null,
        Images = [], Video = [], Files = [], UsersIds = [], Reactions = []
    };

    [Fact]
    public Task PinnedCardMenuNavigatesAndUnpinsWithoutATimelineFlyout() => TestUiThread.RunAsync(() =>
    {
        using var messages = ReactiveCommand.Create<ChatMessageModel>(_ => { });
        using var users = ReactiveCommand.Create<int>(_ => { });
        using var composer = new ChatComposerVM(7, "chat", false, null!, 1, null!, null!, null!,
            _ => Task.CompletedTask) { IsConnected = true };
        var requests = new List<bool>();
        var content = new ChatMessageVM(Message(true), 1, messages, users, messages, messages, users,
            _ => Task.FromResult(true), composer: composer)
        {
            IsPinAllowed = true, CanManageMessages = true,
            SetPinned = pinned => { requests.Add(pinned); return Task.FromResult(true); }
        };
        var navigations = 0;
        var row = new ChatPinnedMessageVM(content, () => { navigations++; return Task.CompletedTask; });
        row.Open();
        Assert.Equal(1, navigations);
        row.Unpin();
        Assert.Equal(new[] { false }, requests);
        Assert.True(content.IsPinned);
        content.CanManageMessages = false;
        row.Unpin();
        Assert.Single(requests);
    });

    [Fact]
    public Task PinnedMessageNavigationClosesMenuBeforeJumpingToTheOriginal() => TestUiThread.RunAsync(() =>
    {
        var actions = new List<string>();
        using var messages = ReactiveCommand.Create<ChatMessageModel>(_ => { });
        using var users = ReactiveCommand.Create<int>(_ => { });
        using var jump = ReactiveCommand.Create<int>(id => { actions.Add($"jump:{id}"); });
        var content = new ChatMessageVM(Message(true), 1, messages, jump, messages, messages, users,
            _ => Task.FromResult(true));
        Assert.False(content.IsPinnedListItem);
        _ = new ChatPinnedMessageVM(content, () => Task.CompletedTask);
        Assert.True(content.IsPinnedListItem);
        using var close = content.CloseFlyoutInteraction.RegisterHandler(context =>
        {
            actions.Add("close");
            context.SetOutput(RxVoid.Default);
        });

        content.GoToMessage();

        Assert.Equal(new[] { "close", "jump:1" }, actions);
    });

    [Fact]
    public Task DirectChatMemberCanPinWithoutDeletingPeerMessages() => TestUiThread.RunAsync(() =>
    {
        using var messages = ReactiveCommand.Create<ChatMessageModel>(_ => { });
        using var users = ReactiveCommand.Create<int>(_ => { });
        using var composer = new ChatComposerVM(7, "chat", false, null!, 1, null!, null!, null!,
            _ => Task.CompletedTask) { IsConnected = true };
        var requested = new List<bool>();
        var vm = new ChatMessageVM(Message(), 1, messages, users, messages, messages, users,
            _ => Task.FromResult(true), composer: composer)
        {
            IsPinAllowed = true, CanPinInDirectChat = true,
            SetPinned = pinned => { requested.Add(pinned); return Task.FromResult(true); }
        };
        using var close = vm.CloseFlyoutInteraction.RegisterHandler(context => context.SetOutput(RxVoid.Default));
        Assert.True(vm.CanPin);
        Assert.False(vm.CanDelete);
        vm.TogglePinned();
        Assert.Equal(new[] { true }, requested);
        composer.IsConnected = false;
        Assert.False(vm.CanPin);
    });

    [Fact]
    public Task PinningRequiresModerationAndConnection_AndWaitsForServerState() => TestUiThread.RunAsync(() =>
    {
        using var messages = ReactiveCommand.Create<ChatMessageModel>(_ => { });
        using var users = ReactiveCommand.Create<int>(_ => { });
        using var composer = new ChatComposerVM(7, "chat", false, null!, 1, null!, null!, null!,
            _ => Task.CompletedTask) { CanWrite = false, IsConnected = true };
        var requested = new List<bool>();
        var vm = new ChatMessageVM(Message(), 1, messages, users, messages, messages, users,
            _ => Task.FromResult(true), composer: composer)
        {
            IsPinAllowed = true,
            SetPinned = pinned => { requested.Add(pinned); return Task.FromResult(true); }
        };
        using var close = vm.CloseFlyoutInteraction.RegisterHandler(context => context.SetOutput(RxVoid.Default));
        Assert.False(vm.CanPin);
        vm.CanManageMessages = true;
        Assert.True(vm.CanPin); // Moderators can manage pins in a read-only chat.
        vm.TogglePinned();
        Assert.Equal(new[] { true }, requested);
        Assert.False(vm.IsPinned);
        vm.Message = Message(true);
        Assert.True(vm.IsPinned);
        vm.TogglePinned();
        Assert.Equal(new[] { true, false }, requested);
        composer.IsConnected = false;
        vm.TogglePinned();
        Assert.Equal(2, requested.Count);
        composer.IsConnected = true;
        vm.IsPinAllowed = false;
        Assert.False(vm.CanPin);
        vm.TogglePinned();
        Assert.Equal(2, requested.Count);
    });
}
