using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Collections;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// The rows the chat list shows: messages sorted by time, grouped into sender chains
/// and split by date separators.
/// Rows are updated in place rather than reloaded, so the list keeps its containers and
/// scroll position when a page is prepended or a single message arrives.
/// </summary>
public class ChatTimeline
{
    // Messages from one sender closer than this are drawn as one chain
    private static readonly TimeSpan ChainGap = TimeSpan.FromMinutes(2);

    public AvaloniaList<IMessageListItemVM> Items { get; } = new();

    // Separators are reused so that an update only touches rows that really changed
    private readonly Dictionary<DateTime, ChatDateSeparatorVM> _separators = new();

    public void Update(IEnumerable<ChatMessageVM> messages)
    {
        var sorted = messages
            .OrderBy(vm => vm.Message.SentTime)
            .ThenBy(vm => vm.Message.Id)
            .ToList();

        UpdateChains(sorted);
        Apply(BuildRows(sorted));
    }

    private List<IMessageListItemVM> BuildRows(List<ChatMessageVM> sorted)
    {
        var rows = new List<IMessageListItemVM>(sorted.Count + 8);
        var usedDates = new HashSet<DateTime>();

        foreach (var messageVm in sorted)
        {
            var messageDate = messageVm.Message.SentTime.Date;
            if (usedDates.Add(messageDate))
            {
                if (!_separators.TryGetValue(messageDate, out var separator))
                {
                    separator = new ChatDateSeparatorVM(messageVm.Message.SentTime);
                    _separators.Add(messageDate, separator);
                }

                rows.Add(separator);
            }

            rows.Add(messageVm);
        }

        foreach (var date in _separators.Keys.Where(date => !usedDates.Contains(date)).ToList())
            _separators.Remove(date);

        return rows;
    }

    /// <summary>
    /// Turns <see cref="Items"/> into <paramref name="rows"/> with removals and range inserts.
    /// Rows keep their relative order between updates, so what stays is a subsequence of the new rows.
    /// </summary>
    private void Apply(List<IMessageListItemVM> rows)
    {
        var keep = new HashSet<IMessageListItemVM>(rows, ReferenceEqualityComparer.Instance);
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(Items[i]))
                Items.RemoveAt(i);
        }

        int target = 0;
        int row = 0;
        while (row < rows.Count)
        {
            if (target < Items.Count && ReferenceEquals(Items[target], rows[row]))
            {
                target++;
                row++;
                continue;
            }

            int start = row;
            while (row < rows.Count && !(target < Items.Count && ReferenceEquals(Items[target], rows[row])))
                row++;

            Items.InsertRange(target, rows.GetRange(start, row - start));
            target += row - start;
        }

        // Safety net for an order change the in-place update cannot express
        if (Items.Count != rows.Count)
        {
            Items.Clear();
            Items.AddRange(rows);
        }
    }

    private static void UpdateChains(List<ChatMessageVM> sorted)
    {
        for (int i = 0; i < sorted.Count; i++)
        {
            var current = sorted[i];
            var previous = i > 0 ? sorted[i - 1] : null;
            var next = i < sorted.Count - 1 ? sorted[i + 1] : null;

            current.IsSequenceStart = !(previous != null && !previous.Message.IsDeleted && IsChained(previous, current));
            current.IsSequenceEnd = !(next != null && !next.Message.IsDeleted && IsChained(current, next));
        }
    }

    private static bool IsChained(ChatMessageVM earlier, ChatMessageVM later)
    {
        return earlier.Message.SenderUserId == later.Message.SenderUserId
               && later.Message.SentTime - earlier.Message.SentTime <= ChainGap;
    }
}
