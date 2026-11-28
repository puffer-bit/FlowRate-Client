using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Threading;
using Client.Application.Tasks;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Tasks;

/// <summary>
/// The work in progress the user started, for the status bar and its flyout. Only what runs long enough
/// to be waited for is shown; what is done leaves after a moment, a failure stays until dismissed.
/// </summary>
public sealed class TasksVM : ReactiveObject
{
    /// <summary>Shorter work is never shown, unless it failed.</summary>
    public static readonly TimeSpan ShowAfter = TimeSpan.FromSeconds(1);

    /// <summary>How long a finished task stays in the list.</summary>
    public static readonly TimeSpan FinishedLinger = TimeSpan.FromSeconds(4);

    /// <summary>How long a row fades out before it is taken off the list.</summary>
    public static readonly TimeSpan LeaveDuration = TimeSpan.FromMilliseconds(250);

    // The rows change no more often than this, whatever the rate of the reports
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(100);

    private readonly ITaskTracker _tracker;
    private readonly Func<DateTimeOffset> _now;
    private readonly TimeSpan _leave;
    private readonly DispatcherTimer? _timer;

    // Begun but not running long enough to be shown yet
    private readonly List<ITrackedTask> _pending = [];

    public ObservableCollection<TrackedTaskVM> Items { get; } = [];

    public bool HasItems
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public int RunningCount
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsRunning
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool HasFailed
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>The segment of the status bar tells a count: something runs, or only failures are left.</summary>
    public bool HasSummary
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>Nothing runs and nothing failed: only finished tasks are left, the bar shows a check.</summary>
    public bool IsDone
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>Nothing runs and something failed: the bar shows the warning.</summary>
    public bool IsFailedOnly
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool HasEnded
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>The share done of all running tasks that tell one, from 0 to 1.</summary>
    public double Progress
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>No running task tells how far it got.</summary>
    public bool IsIndeterminate
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>The segment of the status bar: "2 · 45%", or the count alone.</summary>
    public string SummaryText
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    public ReactiveCommand<RxVoid, RxVoid> ClearEndedCommand { get; }

    /// <param name="now">The clock; tests pass their own and call <see cref="Tick"/> instead of the timer.</param>
    public TasksVM(ITaskTracker tracker, Func<DateTimeOffset>? now = null)
    {
        _tracker = tracker;
        _now = now ?? (() => DateTimeOffset.Now);
        // Tests see a row gone at once
        _leave = now == null ? LeaveDuration : TimeSpan.Zero;
        if (now == null)
            _timer = new DispatcherTimer(RefreshInterval, DispatcherPriority.Background, (_, _) => Tick());

        // Lives as long as the application, as the tracker does
        tracker.Started += task => OnUiThread(() => Add(task));
        tracker.Ended += _ => OnUiThread(Tick);
        foreach (var task in tracker.Running)
            Add(task);

        ClearEndedCommand = ReactiveCommand.Create(() =>
        {
            foreach (var item in Items.Where(item => item.IsEnded).ToList())
                Remove(item);
            Summarize();
        });
    }

    private static void OnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    private void Add(ITrackedTask task)
    {
        if (_pending.Contains(task) || Items.Any(item => item.Task == task))
            return;
        _pending.Add(task);
        _timer?.Start();
    }

    /// <summary>Takes the state of the tasks. Called on the UI thread by the timer, and by tests.</summary>
    public void Tick()
    {
        var now = _now();

        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            var task = _pending[i];
            var isRunning = task.State == TrackedTaskState.Running;
            if (isRunning && now - task.StartedAt < ShowAfter)
                continue;

            _pending.RemoveAt(i);
            // Quick work that went well is not worth a row
            if (isRunning || task.State == TrackedTaskState.Failed)
                Items.Add(new TrackedTaskVM(task, now, Dismiss));
        }

        foreach (var item in Items.ToList())
        {
            if (item.LeftAt is { } leftAt)
            {
                if (now - leftAt >= _leave)
                    Items.Remove(item);
                continue;
            }

            item.Refresh(now);
            if (item.EndedAt is { } endedAt && !item.IsFailed && now - endedAt >= FinishedLinger)
                Remove(item);
        }

        Summarize();

        // Failures stay without changing: nothing is left to follow
        if (_pending.Count == 0 && Items.All(item => item.IsFailed && !item.IsLeaving))
            _timer?.Stop();
    }

    private void Dismiss(TrackedTaskVM item)
    {
        Remove(item);
        Summarize();
    }

    // The row fades out first; the timer takes it off the list once it has
    private void Remove(TrackedTaskVM item)
    {
        if (_leave == TimeSpan.Zero)
        {
            Items.Remove(item);
            return;
        }

        item.Leave(_now());
        _timer?.Start();
    }

    private void Summarize()
    {
        // A row fading out is gone already for the counts
        var items = Items.Where(item => !item.IsLeaving).ToList();
        var running = items.Where(item => item.IsRunning).ToList();
        var measured = running.Where(item => !item.IsIndeterminate).ToList();

        HasItems = items.Count > 0;
        RunningCount = running.Count;
        IsRunning = running.Count > 0;
        HasFailed = items.Any(item => item.IsFailed);
        HasEnded = items.Any(item => item.IsEnded);
        HasSummary = IsRunning || HasFailed;
        IsDone = !IsRunning && !HasFailed && HasItems;
        IsFailedOnly = !IsRunning && HasFailed;
        IsIndeterminate = running.Count > 0 && measured.Count == 0;
        Progress = measured.Count > 0 ? measured.Average(item => item.Progress) : 0;

        SummaryText = running.Count == 0
            ? items.Count(item => item.IsFailed) is > 0 and var failed ? failed.ToString(CultureInfo.InvariantCulture) : string.Empty
            : measured.Count > 0
                ? $"{running.Count} · {(Progress * 100).ToString("0", CultureInfo.InvariantCulture)}%"
                : running.Count.ToString(CultureInfo.InvariantCulture);
    }
}
