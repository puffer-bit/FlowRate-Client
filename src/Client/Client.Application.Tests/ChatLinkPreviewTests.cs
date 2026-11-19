using System.Reactive.Linq;
using Client.Application.Coordinator;
using Client.Core.Chat;
using Client.Core.Coordinator;
using Client.Core.Users;
using Client.ViewModels.Sessions.Chat;
using System.Reflection;
using Xunit;

namespace Client.Application.Tests;

public sealed class ChatLinkPreviewTests
{
    private const int ChatId = 7;

    private static readonly ChatLinkPreview Preview = new()
    {
        Id = 3, Url = "https://example.com/a", SiteName = "Example", Title = "Title", Description = "Text"
    };

    /// <summary>Answers the requests of the composer; anything else fails the test.</summary>
    public class LinkSession : DispatchProxy
    {
        public List<NewChatMessage> Sent { get; } = [];
        public List<bool?> Edits { get; } = [];
        public int Requests;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "RequestLinkPreview":
                    Interlocked.Increment(ref Requests);
                    return Task.FromResult<RequestResult<ChatLinkPreview?>>(
                        (string)args![1]! == Preview.Url ? Preview : (ChatLinkPreview?)null);
                case "SendChatMessage":
                    Sent.Add((NewChatMessage)args![0]!);
                    return Task.FromResult(new RequestResult(null));
                case "EditChatMessage":
                    Edits.Add((bool?)args![3]);
                    return Task.FromResult(new RequestResult(null));
                case "SetChatTyping":
                    return Task.FromResult(new RequestResult(null));
                case "GetCurrentUser":
                    return new UserModel { Id = 1, Username = "me", CoordinatorInstanceId = "c" };
                default:
                    if (method.Name.StartsWith("add_") || method.Name.StartsWith("remove_")) return null;
                    throw new InvalidOperationException(method.Name);
            }
        }
    }

    private readonly ICoordinatorSession _session = DispatchProxy.Create<ICoordinatorSession, LinkSession>();
    private LinkSession Fake => (LinkSession)(object)_session;
    private List<NewChatMessage> _sent => Fake.Sent;
    private List<bool?> _edits => Fake.Edits;
    private int _requests => Fake.Requests;

    private ChatComposerVM Composer() =>
        new(ChatId, "chat", false, _session, 1, null!, null!, new ChatSenderDirectory(_session, 1),
            _ => Task.CompletedTask)
        {
            IsConnected = true,
            LinkPreviewDelay = TimeSpan.Zero
        };

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task TheLinkBeingTyped_ShowsItsPreview_AndTheMessageCarriesIt()
    {
        using var composer = Composer();

        composer.InputtedText = "look https://example.com/a#part";
        Assert.Equal("https://example.com/a", composer.LinkPreviewTarget);
        await Until(() => composer.ComposerLinkPreview != null);

        Assert.True(composer.IsLinkPreviewBarVisible);
        Assert.Equal("Title", composer.ComposerLinkPreview!.Title);

        await composer.SendMessageCommand.Execute();
        Assert.False(Assert.Single(_sent).IsLinkPreviewDisabled);
        Assert.Null(composer.LinkPreviewTarget);
    }

    [Fact]
    public async Task ClosingThePreview_SendsTheMessageWithoutIt_UntilAnotherLink()
    {
        using var composer = Composer();
        composer.InputtedText = "https://example.com/a";
        await Until(() => composer.ComposerLinkPreview != null);

        composer.CloseLinkPreviewCommand.Execute().Subscribe();
        Assert.False(composer.IsLinkPreviewBarVisible);

        composer.InputtedText = "https://example.com/a and more text";
        Assert.False(composer.IsLinkPreviewBarVisible);

        await composer.SendMessageCommand.Execute();
        Assert.True(Assert.Single(_sent).IsLinkPreviewDisabled);

        // The next message starts afresh
        composer.InputtedText = "https://example.com/a";
        await Until(() => composer.ComposerLinkPreview != null);
        Assert.True(composer.IsLinkPreviewBarVisible);
    }

    [Fact]
    public async Task APageWithoutPreview_ShowsNoBar_AndIsNotAskedTwice()
    {
        using var composer = Composer();

        composer.InputtedText = "https://nothing.example/";
        await Until(() => !composer.IsLinkPreviewLoading);
        Assert.False(composer.IsLinkPreviewBarVisible);

        composer.InputtedText = "";
        composer.InputtedText = "https://nothing.example/";
        Assert.False(composer.IsLinkPreviewLoading);
        Assert.Equal(1, _requests);
    }

    [Fact]
    public async Task AnEdit_SendsTheFlagOnlyWhenItChanges()
    {
        using var composer = Composer();
        var message = new ChatMessageModel
        {
            Id = 5, ChatId = ChatId, SenderUserId = 1, SentTime = DateTime.UtcNow,
            TextContent = new ChatTextContent { Text = "https://example.com/a" },
            Images = [], Video = [], Files = [], Reactions = [], UsersIds = [],
            LinkPreview = Preview, IsLinkPreviewDisabled = false, IsMine = true
        };

        composer.StartEdit(message);
        Assert.Equal(Preview.Id, composer.ComposerLinkPreview?.Preview.Id);
        composer.InputtedText = "https://example.com/a!";
        await composer.SendMessageCommand.Execute();

        composer.StartEdit(message);
        composer.CloseLinkPreviewCommand.Execute().Subscribe();
        await composer.SendMessageCommand.Execute();

        Assert.Equal(new bool?[] { null, true }, _edits);
    }
}
