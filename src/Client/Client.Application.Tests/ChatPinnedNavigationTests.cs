using System.Reflection;
using Avalonia.Threading;
using Client.Application.Chat;
using Client.Application.Coordinator;
using Client.Application.Ports;
using Client.Core.Chat;
using Client.Core.Coordinator;
using Client.Core.Sessions;
using Client.Core.Users;
using Client.ViewModels.Sessions;
using Client.ViewModels.Sessions.Chat;
using ReactiveUI;
using Shared.Enums;
using Shared.Models.Requests.SessionActions.ChatSession;
using Xunit;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.Application.Tests;

public class ChatPinnedNavigationTests
{
    private static ChatMessageModel Message(int id, long revision = 100, string text = "text", bool pinned = false, ChatImageContent? image = null, ChatFileContent? file = null) => new()
    {
        Id = id, ChatId = 7, SenderUserId = 2, SentTime = DateTime.UnixEpoch.AddSeconds(id),
        Revision = revision, TextContent = new ChatTextContent { Text = text },
        PinnedAt = pinned ? DateTime.UnixEpoch : null, PinnedByUserId = pinned ? 1 : null,
        Images = image == null ? [] : [image], Video = [], Files = file == null ? [] : [file], Reactions = [], UsersIds = []
    };

    [Fact]
    public Task HistoricalWindowSeparatesLiveMessagesAndMergesEditsAndDeletesBeforeLoadingDown() => TestUiThread.RunAsync(() =>
    {
        using var users = ReactiveCommand.Create<int>(_ => { });
        var session = ChatHistoryWindowTests.Proxy<ICoordinatorSession>((method, args) => method switch
        {
            nameof(ICoordinatorSession.GetCurrentUser) => new UserModel { Id = 1, Username = "me", CoordinatorInstanceId = "server" },
            "get_Permissions" => CoordinatorPermissions.None,
            "get_ConnectionStatus" => CoordinatorState.Connected,
            nameof(ICoordinatorSession.GetChatTypingUsers) => new Dictionary<int, DateTimeOffset>(),
            nameof(ICoordinatorSession.GetUsers) => Task.FromResult<IReadOnlyDictionary<int, UserModel>>(new Dictionary<int, UserModel>()),
            nameof(ICoordinatorSession.IsUserDeleted) => false,
            nameof(ICoordinatorSession.RequestChatHistoryWindow) => Task.FromResult(new RequestResult<ChatHistoryWindow>(
                (ChatHistoryDirection)args![2]! == ChatHistoryDirection.Around
                    ? new([Message(2), Message(3), Message(4)], 100, true, true)
                    : new(Enumerable.Range(5, 7).Select(id => Message(id)).ToList(), 103, true, false))),
            _ when method.StartsWith("add_") || method.StartsWith("remove_") => null,
            _ => throw new InvalidOperationException(method)
        });
        var cache = ChatHistoryWindowTests.Proxy<IChatCache>((_, _) => null);
        var caches = ChatHistoryWindowTests.Proxy<IChatCacheStore>((_, _) => cache);
        var mediaCache = ChatHistoryWindowTests.Proxy<IMediaCache>((_, _) => null);
        using var chat = new ActiveChatSessionVM(new ChatSessionModel
        {
            Id = 7, Name = "chat", CreatorUserId = 1, Capacity = null, SessionType = SessionType.Chat
        }, session, new ChatSessionServices(caches, null!, null!, null!, mediaCache), new ChatReadTracker(), users);
        using var scroll = chat.ScrollToMessageIndexInteraction.RegisterHandler(context => context.SetOutput(RxVoid.Default));
        typeof(ActiveChatSessionVM).GetProperty(nameof(chat.IsInitialLoading))!.SetValue(chat, false);
        typeof(ActiveChatSessionVM).GetField("_revision", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(chat, 100L);
        Call(chat, "AddMessages", new[] { Message(10) }, false);
        var jump = (Task)Call(chat, "JumpToMessage", 3)!;
        Assert.True(jump.IsCompletedSuccessfully);
        Assert.Equal(new[] { 2, 3, 4 }, chat.Messages.OfType<ChatMessageVM>().Select(x => x.Message.Id));
        Assert.True(chat.HasNewerMessages);

        Call(chat, "OnTextMessageAdded", 7, Message(11, 101));
        Call(chat, "OnTextMessageAdded", 7, Message(11, 101));
        const string fullText = "updated pin\nsecond line\nthird line\nfourth line";
        var image = new ChatImageContent
        { Id = 40, FileName = "image.png", Size = 10, MimeType = "image/png", Width = 100, Height = 80 };
        var file = new ChatFileContent { Id = 41, FileName = "document.pdf", Size = 30, MimeType = "application/pdf" };
        Call(chat, "OnTextMessageEdited", 7, Message(5, 102, fullText, true, image, file));
        Call(chat, "OnTextMessageDeleted", 7, 6, 103L);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(chat.Messages.OfType<ChatMessageVM>(), x => x.Message.Id == 11);
        Assert.Equal(1, chat.UnreadBelowCount);
        var pin = Assert.Single(chat.PinnedMessages);
        Assert.True(pin.Content.IsSequenceStart);
        Assert.True(pin.Content.IsSequenceEnd);
        Assert.Equal(fullText, pin.Preview);
        Assert.Equal(40, Assert.Single(pin.Content.Images).Image.Id);
        Assert.Equal(41, Assert.Single(pin.Content.Files).File.Id);
        Call(chat, "PublishPins");
        Assert.Same(pin, Assert.Single(chat.PinnedMessages));

        var newer = chat.LoadNewerMessagesAsync();
        Assert.True(newer.IsCompletedSuccessfully);
        Assert.False(chat.HasNewerMessages);
        var loaded = chat.Messages.OfType<ChatMessageVM>().ToArray();
        Assert.DoesNotContain(loaded, x => x.Message.Id == 6);
        Assert.Equal(fullText, loaded.Single(x => x.Message.Id == 5).Message.TextContent!.Text);
        Assert.Contains(loaded, x => x.Message.Id == 11);

        Call(chat, "OnTextMessageAdded", 7, Message(12, 104));
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(chat.Messages.OfType<ChatMessageVM>(), x => x.Message.Id == 12);
        Call(chat, "OnTextMessageDeleted", 7, 5, 105L);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(chat.PinnedMessages);
    });

    private static object? Call(ActiveChatSessionVM chat, string name, params object?[] args) =>
        typeof(ActiveChatSessionVM).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(chat, args);
}
