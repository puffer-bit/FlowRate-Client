using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Collections;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// Messages ordered by time and id, with date separators and sender chains.
/// Inserts and removals keep existing rows and update only the neighboring chains.
/// Used on the UI thread only; message ids and sent times stay fixed while loaded.
/// </summary>
public class ChatTimeline
{
    private static readonly TimeSpan ChainGap = TimeSpan.FromMinutes(2);
    private static readonly IComparer<ChatMessageVM> MessageOrder = Comparer<ChatMessageVM>.Create((left, right) =>
    {
        var time = left.Message.SentTime.CompareTo(right.Message.SentTime);
        return time != 0 ? time : left.Message.Id.CompareTo(right.Message.Id);
    });

    public AvaloniaList<IMessageListItemVM> Items { get; } = new();

    private readonly List<ChatMessageVM> _messages = new();
    private readonly SortedList<DateTimeOffset, ChatDateSeparatorVM> _separators = new();

    /// <summary>Adds messages not already in the timeline, sorting only the incoming batch.</summary>
    public void AddRange(IReadOnlyList<ChatMessageVM> messages)
    {
        if (messages.Count == 0)
            return;

        if (messages.Count == 1)
        {
            InsertRange(FindPosition(messages[0]), messages);
            return;
        }

        var added = messages.ToList();
        added.Sort(MessageOrder);
        var start = 0;
        while (start < added.Count)
        {
            var position = FindPosition(added[start]);
            var date = added[start].LocalSentTime.Date;
            var end = start + 1;
            // A run on one date before the next existing message is inserted in one notification.
            while (end < added.Count && added[end].LocalSentTime.Date == date &&
                   (position == _messages.Count || MessageOrder.Compare(added[end], _messages[position]) < 0))
                end++;

            InsertRange(position, added.GetRange(start, end - start));
            start = end;
        }
    }

    public void Remove(ChatMessageVM message)
    {
        var position = _messages.BinarySearch(message, MessageOrder);
        // Equal times and ids cannot be told apart by the order: the row is the one holding this message
        if (position >= 0 && !ReferenceEquals(_messages[position], message))
            position = _messages.IndexOf(message);
        if (position < 0)
            return;

        RemoveAt(position);
    }

    private void RemoveAt(int position)
    {
        var message = _messages[position];
        var date = message.LocalSentTime.Date;
        var day = FindDatePosition(date);
        var row = position + day + 1;
        _messages.RemoveAt(position);
        UpdateChain(position - 1);
        UpdateChain(position);
        Items.RemoveAt(row);

        if ((position == 0 || _messages[position - 1].LocalSentTime.Date != date) &&
            (position == _messages.Count || _messages[position].LocalSentTime.Date != date))
        {
            _separators.RemoveAt(day);
            Items.RemoveAt(row - 1);
        }
    }

    /// <summary>
    /// Puts a message in the row of another one, as the stored copy of a message takes the row it was shown in while
    /// on its way. The row stays where it is when the order allows it; otherwise the message moves to its place.
    /// </summary>
    public void Replace(ChatMessageVM shown, ChatMessageVM replacement)
    {
        // Rows on their way are at the end, so the search starts there
        var position = _messages.LastIndexOf(shown);
        if (position < 0)
        {
            AddRange([replacement]);
            return;
        }

        var date = shown.LocalSentTime.Date;
        var fits = replacement.LocalSentTime.Date == date
                   && (position == 0 || MessageOrder.Compare(_messages[position - 1], replacement) < 0)
                   && (position == _messages.Count - 1 || MessageOrder.Compare(replacement, _messages[position + 1]) < 0);
        if (!fits)
        {
            RemoveAt(position);
            AddRange([replacement]);
            return;
        }

        var row = position + FindDatePosition(date) + 1;
        _messages[position] = replacement;
        UpdateChain(position - 1);
        UpdateChain(position);
        UpdateChain(position + 1);
        Items[row] = replacement;
    }

    public void Clear()
    {
        _messages.Clear();
        _separators.Clear();
        Items.Clear();
    }

    private int FindPosition(ChatMessageVM message)
    {
        // Incoming messages normally continue the tail; appending needs no search or shifts.
        if (_messages.Count == 0 || MessageOrder.Compare(_messages[^1], message) < 0)
            return _messages.Count;

        var position = _messages.BinarySearch(message, MessageOrder);
        return position < 0 ? ~position : position;
    }

    private int FindDatePosition(DateTimeOffset date)
    {
        var low = 0;
        var high = _separators.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (_separators.Keys[middle] < date)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private void InsertRange(int position, IReadOnlyList<ChatMessageVM> messages)
    {
        var date = messages[0].LocalSentTime.Date;
        var day = FindDatePosition(date);
        var row = position + day;
        var hasSeparator = day < _separators.Count && _separators.Keys[day] == date;
        if (hasSeparator)
            row++;

        _messages.InsertRange(position, messages);
        for (var i = Math.Max(0, position - 1); i <= position + messages.Count && i < _messages.Count; i++)
            UpdateChain(i);

        if (hasSeparator)
        {
            Items.InsertRange(row, messages);
        }
        else
        {
            var separator = new ChatDateSeparatorVM(date);
            _separators.Add(date, separator);
            var rows = new List<IMessageListItemVM>(messages.Count + 1) { separator };
            rows.AddRange(messages);
            Items.InsertRange(row, rows);
        }
    }

    private void UpdateChain(int position)
    {
        if (position < 0 || position >= _messages.Count)
            return;

        var current = _messages[position];
        var previous = position > 0 ? _messages[position - 1] : null;
        var next = position + 1 < _messages.Count ? _messages[position + 1] : null;
        current.IsSequenceStart = !(previous != null && !previous.Message.IsDeleted && IsChained(previous, current));
        current.IsSequenceEnd = !(next != null && !next.Message.IsDeleted && IsChained(current, next));
    }

    private static bool IsChained(ChatMessageVM earlier, ChatMessageVM later) =>
        !earlier.Message.IsSystem && !later.Message.IsSystem &&
        earlier.Message.SenderUserId == later.Message.SenderUserId &&
        later.Message.SentTime - earlier.Message.SentTime <= ChainGap;
}
