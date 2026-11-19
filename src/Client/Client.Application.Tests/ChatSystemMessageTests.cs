using System.Reflection;
using Avalonia.Threading;
using Client.Application.Coordinator;
using Client.Core.Coordinator;
using Client.Core.Users;
using ReactiveUI;
using Client.Api.Mappers;
using Client.Avalonia.Localization;
using Client.Core.Chat;
using Client.ViewModels.Sessions.Chat;
using Shared.Models.DataTransferObjects.ChatSession.Messages;
using Shared.Models.DataTransferObjects.ChatSession.Messages.Content;
using Xunit;

namespace Client.Application.Tests;

public sealed class ChatSystemMessageTests
{
    private static ChatSystemContent Content(ChatSystemEventKind kind) => new()
    {
        Kind = kind, ActorUserId = 1,
        SubjectUserId = kind is ChatSystemEventKind.CallStarted or ChatSystemEventKind.CallEnded ? null
            : kind == ChatSystemEventKind.MemberLeft ? 1 : 2,
        CallId = kind is ChatSystemEventKind.CallStarted or ChatSystemEventKind.CallEnded ? Guid.NewGuid() : null
    };

    private static ChatMessageModel Message(int id, ChatSystemContent? content = null) => new()
    {
        Id = id, ChatId = 7, SenderUserId = -1, SentTime = DateTime.UtcNow.AddSeconds(id),
        IsSystem = true, IsReadOnly = true, SystemContent = content,
        TextContent = new ChatTextContent { Text = "Legacy server event" },
        Images = [], Video = [], Files = [], Reactions = [], UsersIds = []
    };

    private static ChatMessageVM View(ChatMessageModel message) => new(message, 1,
        null!, null!, null!, null!, null!, _ => Task.FromResult(true));

    [Theory]
    [InlineData(ChatSystemEventKind.MemberAdded)]
    [InlineData(ChatSystemEventKind.MemberRemoved)]
    [InlineData(ChatSystemEventKind.MemberLeft)]
    [InlineData(ChatSystemEventKind.CallStarted)]
    [InlineData(ChatSystemEventKind.CallEnded)]
    public void MappingAndPresentationPreserveServerEvent(ChatSystemEventKind kind)
    {
        var content = Content(kind);
        var model = new ChatMessageDTO
        {
            Id = 1, ChatId = 7, SenderUserId = -1, SentTime = DateTime.UtcNow,
            IsSystem = true, IsReadOnly = true, SystemContent = content,
            TextContent = new ChatTextContentDTO { Text = "Fallback" },
            ReplyContent = null!, Images = [], Video = [], Files = [], Reactions = [], UsersIds = []
        }.ToModel();
        Assert.Equal(content, model.SystemContent);
        var view = View(model);
        Assert.True(view.IsSystem);
        Assert.Equal(kind, view.SystemEventKind);
        Assert.False(view.CanEdit);
        Assert.False(view.CanDelete);
        Assert.False(view.CanReact);
        var text = ChatSystemMessageText.Format(model, id => id == 1 ? "Alex" : "Maria");
        Assert.DoesNotContain("Fallback", text);
        if (kind != ChatSystemEventKind.CallEnded) Assert.Contains("Alex", text);
        if (kind is ChatSystemEventKind.MemberAdded or ChatSystemEventKind.MemberRemoved) Assert.Contains("Maria", text);
        var users = new HashSet<int>();
        ChatSenderDirectory.CollectUserIds(view, users);
        Assert.DoesNotContain(-1, users);
        Assert.Contains(1, users);
        if (content.SubjectUserId == 2) Assert.Contains(2, users);
    }

    [Fact]
    public void UserLinksPreserveIdentityEvenWhenNamesMatchOrContainFormatting()
    {
        var message = Message(1, Content(ChatSystemEventKind.MemberAdded));
        var parts = ChatSystemMessageText.Parts(message, _ => "**same {actor}**");
        Assert.Equal(new int?[] { 1, 2 }, parts.Where(part => part.UserId != null).Select(part => part.UserId));
        Assert.All(parts.Where(part => part.UserId != null), part => Assert.Equal("**same {actor}**", part.Text));
        var deleted = ChatSystemMessageText.Parts(message, _ => "Deleted user", id => id != 2);
        Assert.Equal(1, Assert.Single(deleted, part => part.UserId != null).UserId);
        Assert.Contains(deleted, part => part.Text == "Deleted user" && part.UserId == null);
        Assert.DoesNotContain(ChatSystemMessageText.Parts(message, _ => null), part => part.UserId != null);
    }

    [Fact]
    public Task SystemDirectoryUsesDisplayNamesAndUpdatesThemWhenUserChanges() => TestUiThread.RunAsync(() =>
    {
        var session = DispatchProxy.Create<ICoordinatorSession, EventUsers>();
        var fake = (EventUsers)session;
        fake.Users[1] = new UserModel { Id = 1, CoordinatorInstanceId = "server", Username = "User14", DisplayName = "Alex" };
        fake.Users[2] = new UserModel { Id = 2, CoordinatorInstanceId = "server", Username = "Maria" };
        using var directory = new ChatSenderDirectory(session, 1);
        directory.ResolveAsync([1, 2]).GetAwaiter().GetResult();
        var vm = View(Message(1, Content(ChatSystemEventKind.MemberAdded)));
        directory.Apply(vm);
        Assert.DoesNotContain("User14", vm.SystemText);
        Assert.Equal("Alex", vm.SystemParts.Single(part => part.UserId == 1).Text);
        Assert.Equal("Maria", vm.SystemParts.Single(part => part.UserId == 2).Text);
        fake.RaiseUpdated(new UserModel { Id = 1, CoordinatorInstanceId = "server", Username = "User14", DisplayName = "Alexandra" });
        Dispatcher.UIThread.RunJobs();
        directory.Apply(vm);
        Assert.Equal("Alexandra", vm.SystemParts.Single(part => part.UserId == 1).Text);
        fake.RaiseDeleted(2);
        Dispatcher.UIThread.RunJobs();
        directory.Apply(vm);
        Assert.DoesNotContain(vm.SystemParts, part => part.UserId == 2);
    });

    public class EventUsers : DispatchProxy
    {
        public Dictionary<int, UserModel> Users { get; } = [];
        private Action<UserModel>? _updated;
        private Action<int>? _deleted;
        public void RaiseUpdated(UserModel user) => _updated?.Invoke(user);
        public void RaiseDeleted(int id) => _deleted?.Invoke(id);
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "add_UserUpdated": _updated += (Action<UserModel>)args![0]!; return null;
                case "remove_UserUpdated": _updated -= (Action<UserModel>)args![0]!; return null;
                case "add_UserDeleted": _deleted += (Action<int>)args![0]!; return null;
                case "remove_UserDeleted": _deleted -= (Action<int>)args![0]!; return null;
                case "GetUsers": return Task.FromResult<IReadOnlyDictionary<int, UserModel>>(Users);
                case "get_Permissions": return new CoordinatorPermissions([], []);
                case "IsUserDeleted": return false;
                default: return null;
            }
        }
    }

    [Fact]
    public void LegacyAndUnknownEventsUseTheirTextWithGenericIcon()
    {
        var message = Message(1);
        Assert.Equal("Legacy server event", ChatSystemMessageText.Format(message, _ => null));
        Assert.Equal((ChatSystemEventKind)0, View(message).SystemEventKind);
        message = Message(2, Content((ChatSystemEventKind)999));
        Assert.Equal("Legacy server event", ChatSystemMessageText.Format(message, _ => null));
        Assert.Equal((ChatSystemEventKind)0, View(message).SystemEventKind);
    }

    [Fact]
    public void ConsecutiveSystemEventsNeverChainAndKeepTheirOwnTimes()
    {
        var timeline = new ChatTimeline();
        var first = View(Message(1, Content(ChatSystemEventKind.MemberAdded)));
        var second = View(Message(2, Content(ChatSystemEventKind.MemberRemoved)));
        timeline.AddRange([first, second]);
        Assert.True(first.IsSequenceStart);
        Assert.True(first.IsSequenceEnd);
        Assert.True(second.IsSequenceStart);
        Assert.True(second.IsSequenceEnd);
        Assert.NotEqual(first.LocalSentTime, second.LocalSentTime);
    }

    [Fact]
    public Task EventDescriptionsFollowCurrentLanguageAndTreatNamesAsPlainText() => TestUiThread.RunAsync(() =>
    {
        var original = Strings.Current.Code;
        try
        {
            var message = Message(1, Content(ChatSystemEventKind.MemberAdded));
            Strings.SetLanguage("en");
            Assert.Equal("**Alex** added <Maria>", ChatSystemMessageText.Format(message, id => id == 1 ? "**Alex**" : "<Maria>"));
            Strings.SetLanguage("ru");
            var text = ChatSystemMessageText.Format(message, id => id == 1 ? "Алексей" : "Мария");
            Assert.Contains("Алексей", text);
            Assert.Contains("Мария", text);
            Assert.DoesNotContain("added", text);
        }
        finally { Strings.SetLanguage(original); }
    });
}
