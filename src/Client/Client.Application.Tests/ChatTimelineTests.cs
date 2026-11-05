using System.Collections.Specialized;
using Client.Core.Chat;
using Client.ViewModels.Sessions.Chat;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using Xunit;

namespace Client.Application.Tests;

public sealed class ChatTimelineTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 11, 1, 12, 0, 0);
    private readonly ReactiveCommand<ChatMessageModel, RxVoid> _messageCommand =
        ReactiveCommand.Create<ChatMessageModel>(_ => { });
    private readonly ReactiveCommand<int, RxVoid> _userCommand = ReactiveCommand.Create<int>(_ => { });

    [Fact]
    public void AppendKeepsRowsAndUpdatesTheLastChain()
    {
        var timeline = new ChatTimeline();
        var first = Message(1, Start);
        var second = Message(2, Start.AddMinutes(1));
        timeline.AddRange([first]);
        var separator = timeline.Items[0];
        var changes = Observe(timeline);

        timeline.AddRange([second]);

        Assert.Same(separator, timeline.Items[0]);
        Assert.Same(first, timeline.Items[1]);
        AssertTimeline(timeline, [first, second]);
        var change = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Add, change.Action);
        Assert.Equal(2, change.NewStartingIndex);
        Assert.Same(second, Assert.Single(change.NewItems!.Cast<object>()));
    }

    [Fact]
    public void OlderPageIsInsertedAsARangeAfterTheExistingSeparator()
    {
        var timeline = new ChatTimeline();
        var newest = Message(3, Start.AddMinutes(2));
        var first = Message(1, Start);
        var second = Message(2, Start.AddMinutes(1));
        timeline.AddRange([newest]);
        var separator = timeline.Items[0];
        var changes = Observe(timeline);

        timeline.AddRange([second, first]);

        AssertTimeline(timeline, [first, second, newest]);
        Assert.Same(separator, timeline.Items[0]);
        var change = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Add, change.Action);
        Assert.Equal(1, change.NewStartingIndex);
        Assert.Equal(new[] { first, second }, change.NewItems!.Cast<ChatMessageVM>());
    }

    [Fact]
    public void InterleavedBatchAcrossDatesKeepsExistingSeparators()
    {
        var timeline = new ChatTimeline();
        var middle = Message(4, Start.AddDays(1).AddMinutes(1));
        var last = Message(7, Start.AddDays(3));
        timeline.AddRange([middle, last]);
        var middleSeparator = timeline.Items[0];
        var lastSeparator = timeline.Items[2];
        var additions = new[]
        {
            Message(1, Start.AddDays(-1)), Message(2, Start),
            Message(3, Start.AddDays(1)), Message(5, Start.AddDays(1).AddMinutes(2)),
            Message(6, Start.AddDays(2)), Message(8, Start.AddDays(4))
        };

        timeline.AddRange(additions.Reverse().ToArray());

        AssertTimeline(timeline, additions.Concat([middle, last]));
        Assert.Same(middleSeparator, timeline.Items[4]);
        Assert.Same(lastSeparator, timeline.Items[10]);
    }

    [Fact]
    public void SameTimestampUsesMessageIdAndRemovalJoinsNeighboringChains()
    {
        var timeline = new ChatTimeline();
        var first = Message(1, Start);
        var middle = Message(2, Start, sender: 2);
        var last = Message(3, Start);
        timeline.AddRange([last, first]);
        timeline.AddRange([middle]);
        AssertTimeline(timeline, [first, middle, last]);
        Assert.True(first.IsSequenceEnd);
        Assert.True(last.IsSequenceStart);

        timeline.Remove(middle);

        AssertTimeline(timeline, [first, last]);
        Assert.False(first.IsSequenceEnd);
        Assert.False(last.IsSequenceStart);
    }

    [Fact]
    public void RemovingLastMessageOnADateRemovesOnlyItsSeparator()
    {
        var timeline = new ChatTimeline();
        var messages = new[] { Message(1, Start), Message(2, Start.AddDays(1)), Message(3, Start.AddDays(2)) };
        timeline.AddRange(messages);
        var firstSeparator = timeline.Items[0];
        var lastSeparator = timeline.Items[4];

        timeline.Remove(messages[1]);
        AssertTimeline(timeline, [messages[0], messages[2]]);
        Assert.Same(firstSeparator, timeline.Items[0]);
        Assert.Same(lastSeparator, timeline.Items[2]);

        timeline.Remove(messages[0]);
        AssertTimeline(timeline, [messages[2]]);
        timeline.Remove(messages[2]);
        Assert.Empty(timeline.Items);
        timeline.Remove(messages[2]);
        Assert.Empty(timeline.Items);
    }

    [Fact]
    public void ChainGapIncludesTwoMinutesAndPreservesGroupingAcrossMidnight()
    {
        var timeline = new ChatTimeline();
        var first = Message(1, Start.Date.AddHours(23).AddMinutes(59));
        var second = Message(2, first.Message.SentTime.AddMinutes(2));
        var third = Message(3, second.Message.SentTime.AddMinutes(2).AddTicks(1));
        timeline.AddRange([third, second, first]);

        AssertTimeline(timeline, [first, second, third]);
        Assert.False(first.IsSequenceEnd);
        Assert.False(second.IsSequenceStart);
        Assert.True(second.IsSequenceEnd);
        Assert.True(third.IsSequenceStart);
    }

    [Fact]
    public void ClearAllowsLoadingTheSameHistoryAgain()
    {
        var timeline = new ChatTimeline();
        var first = Message(1, Start);
        timeline.AddRange([first]);
        timeline.Clear();
        Assert.Empty(timeline.Items);

        timeline.AddRange([first]);
        AssertTimeline(timeline, [first]);
    }

    [Fact]
    public void MixedHistoryPagesAndDeletionsMatchAFullRebuild()
    {
        var timeline = new ChatTimeline();
        var loaded = new List<ChatMessageVM>();
        var random = new Random(42);
        var nextId = 1;
        for (var operation = 0; operation < 300; operation++)
        {
            if (loaded.Count > 0 && random.Next(3) == 0)
            {
                var index = random.Next(loaded.Count);
                timeline.Remove(loaded[index]);
                loaded.RemoveAt(index);
            }
            else
            {
                var batch = Enumerable.Range(0, random.Next(1, 12))
                    .Select(_ => Message(nextId++, Start.AddMinutes(random.Next(-3000, 3000)), random.Next(1, 4)))
                    .ToArray();
                timeline.AddRange(batch);
                loaded.AddRange(batch);
            }

            AssertTimeline(timeline, loaded);
        }
    }

    private static List<NotifyCollectionChangedEventArgs> Observe(ChatTimeline timeline)
    {
        var changes = new List<NotifyCollectionChangedEventArgs>();
        timeline.Items.CollectionChanged += (_, e) => changes.Add(e);
        return changes;
    }

    private static void AssertTimeline(ChatTimeline timeline, IEnumerable<ChatMessageVM> messages)
    {
        var sorted = messages.OrderBy(vm => vm.Message.SentTime).ThenBy(vm => vm.Message.Id).ToArray();
        var row = 0;
        for (var i = 0; i < sorted.Length; i++)
        {
            if (i == 0 || sorted[i - 1].Message.SentTime.Date != sorted[i].Message.SentTime.Date)
                Assert.IsType<ChatDateSeparatorVM>(timeline.Items[row++]);

            Assert.Same(sorted[i], timeline.Items[row++]);
            Assert.Equal(i == 0 || !Chained(sorted[i - 1], sorted[i]), sorted[i].IsSequenceStart);
            Assert.Equal(i == sorted.Length - 1 || !Chained(sorted[i], sorted[i + 1]), sorted[i].IsSequenceEnd);
        }

        Assert.Equal(row, timeline.Items.Count);
    }

    private static bool Chained(ChatMessageVM earlier, ChatMessageVM later) =>
        earlier.Message.SenderUserId == later.Message.SenderUserId &&
        later.Message.SentTime - earlier.Message.SentTime <= TimeSpan.FromMinutes(2);

    private ChatMessageVM Message(int id, DateTime time, int sender = 1) => new(new ChatMessageModel
    {
        Id = id, ChatId = 1, SenderUserId = sender, SentTime = time,
        Images = [], Video = [], Files = [], Reactions = [], UsersIds = []
    }, 1, _messageCommand, _userCommand, _messageCommand, _messageCommand, _userCommand,
        _ => Task.FromResult(true));

    public void Dispose()
    {
        _messageCommand.Dispose();
        _userCommand.Dispose();
    }
}
