using Client.Core.Chat;
using Client.ViewModels.Sessions.Chat;
using ReactiveUI;
using Xunit;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.Application.Tests;

public sealed class ChatReactionsTests
{
    private static ChatMessageModel Message(params ChatReaction[] reactions) => new()
    {
        Id = 5, ChatId = 7, SenderUserId = 9, SentTime = DateTime.UtcNow,
        Images = [], Files = [], Video = [], UsersIds = [], Reactions = reactions.ToList()
    };

    [Fact]
    public Task CountsSelectionAndToggleFollowServerUpdates() => TestUiThread.RunAsync(() =>
    {
        using var messages = ReactiveCommand.Create<ChatMessageModel>(_ => { });
        using var users = ReactiveCommand.Create<int>(_ => { });
        using var composer = new ChatComposerVM(7, "chat", false, null!, 1, null!, null!, null!,
            _ => Task.CompletedTask) { CanWrite = true, IsConnected = true };
        var sent = new List<(string Emoji, bool Added)>();
        var vm = new ChatMessageVM(Message(new ChatReaction { Emoji = "👍", UserId = 2 }), 1,
            messages, users, messages, messages, users, _ => Task.FromResult(true), composer: composer)
        {
            SetReaction = (emoji, added) =>
            {
                sent.Add((emoji, added));
                return Task.FromResult(true);
            }
        };
        Assert.True(vm.CanReact);
        var reaction = Assert.Single(vm.Reactions);
        Assert.Equal(1, reaction.Count);
        Assert.False(reaction.IsMine);
        reaction.Toggle();
        Assert.Equal(("👍", true), Assert.Single(sent));
        Assert.False(Assert.Single(vm.Reactions).IsMine);

        vm.Message = Message(new ChatReaction { Emoji = "👍", UserId = 2 }, new ChatReaction { Emoji = "👍", UserId = 1 });
        reaction = Assert.Single(vm.Reactions);
        Assert.Equal(2, reaction.Count);
        Assert.True(reaction.IsMine);
        Assert.True(vm.ReactionChoices.Single(r => r.Emoji == "👍").IsMine);
        reaction.Toggle();
        Assert.Equal(("👍", false), sent.Last());

        composer.IsConnected = false;
        reaction.Toggle();
        Assert.False(vm.CanReact);
        Assert.Equal(2, sent.Count);
        composer.IsConnected = true;
        composer.CanWrite = false;
        reaction.Toggle();
        Assert.Equal(2, sent.Count);
        composer.CanWrite = true;
        vm.IsReactionAllowed = false;
        reaction.Toggle();
        Assert.Equal(2, sent.Count);
    });

    [Fact]
    public Task FailureIsVisibleAndDoesNotChangeTheMessage() => TestUiThread.RunAsync(() =>
    {
        using var messages = ReactiveCommand.Create<ChatMessageModel>(_ => { });
        using var users = ReactiveCommand.Create<int>(_ => { });
        using var composer = new ChatComposerVM(7, "chat", false, null!, 1, null!, null!, null!,
            _ => Task.CompletedTask) { CanWrite = true, IsConnected = true };
        var vm = new ChatMessageVM(Message(), 1, messages, users, messages, messages, users,
            _ => Task.FromResult(true), composer: composer)
        {
            SetReaction = (_, _) => Task.FromResult(false)
        };
        vm.ReactionChoices[0].Toggle();
        Assert.True(vm.HasReactionError);
        Assert.False(string.IsNullOrWhiteSpace(vm.ReactionError));
        Assert.Empty(vm.Reactions);
    });
}
