using Avalonia.Threading;
using Client.Avalonia.Styles.Animation;
using Client.Core.Chat;
using Client.ViewModels.Sessions.Chat;
using Shared.Models.DataTransferObjects.ChatSession.Messages.Commands;
using Xunit;

namespace Client.Application.Tests;

public class ChatCommandResultTests
{
    private static ChatMessageModel Message(ChatCommandResult outcome) => new()
    {
        Id = 1, ChatId = 7, SenderUserId = 1, SentTime = DateTime.UtcNow, IsMine = true,
        TextContent = new ChatTextContent { Text = outcome.ToFallbackText(), CommandResult = outcome },
        Images = [], Video = [], Files = [], Reactions = [], UsersIds = []
    };

    private static ChatMessageVM View(ChatCommandResult outcome) => new(Message(outcome), 1,
        null!, null!, null!, null!, null!, _ => Task.FromResult(true));

    private static ChatCommandResult Roll => new()
    {
        Kind = ChatCommandKind.Roll, Minimum = 1, Maximum = 1000, Value = 1000
    };

    [Fact]
    public Task HistoryShowsStoredResultImmediately() => TestUiThread.RunAsync(() =>
    {
        var view = View(Roll);
        Assert.True(view.HasCommandResult);
        Assert.False(view.HasPlainText);
        Assert.False(view.CanEdit);
        Assert.False(view.TakeLiveArrival());
        Assert.False(view.CommandResult!.IsAnimating);
        Assert.Equal("1000", view.CommandResult.DisplayValue);
    });

    [Fact]
    public Task LiveArrivalAnimatesOnceAndMessageUpdatesKeepTheOutcome() => TestUiThread.RunAsync(() =>
    {
        Motion.Apply(true, 0.1);
        try
        {
            var view = View(Roll);
            view.IsNew = true;
            Assert.True(view.TakeLiveArrival());
            var presentation = view.CommandResult!;
            Assert.True(presentation.IsAnimating);
            Assert.False(view.TakeLiveArrival());
            var frame = new DispatcherFrame();
            presentation.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(presentation.IsAnimating) && !presentation.IsAnimating)
                    frame.Continue = false;
            };
            using var timeout = new Timer(_ => Dispatcher.UIThread.Post(() => frame.Continue = false),
                null, TimeSpan.FromSeconds(3), Timeout.InfiniteTimeSpan);
            Dispatcher.UIThread.PushFrame(frame);
            Assert.False(presentation.IsAnimating);
            Assert.Equal("1000", presentation.DisplayValue);
            view.Message = Message(Roll);
            Assert.Same(presentation, view.CommandResult);
            Assert.False(view.TakeLiveArrival());
        }
        finally { Motion.Apply(true, 1); }
    });

    [Fact]
    public Task DisabledAnimationsAndInvalidMetadataNeverAnimate() => TestUiThread.RunAsync(() =>
    {
        Motion.Apply(false, 1);
        try
        {
            var view = View(Roll);
            view.IsNew = true;
            Assert.True(view.TakeLiveArrival());
            Assert.False(view.CommandResult!.IsAnimating);
            Assert.Equal("1000", view.CommandResult.DisplayValue);
            var invalid = View(Roll with { Value = 1001 });
            Assert.False(invalid.HasCommandResult);
            Assert.True(invalid.HasPlainText);
        }
        finally { Motion.Apply(true, 1); }
    });
}
