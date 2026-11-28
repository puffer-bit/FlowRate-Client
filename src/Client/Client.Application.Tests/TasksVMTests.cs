using Client.Application.Tasks;
using Client.ViewModels.Tasks;
using Xunit;

namespace Client.Application.Tests;

public class TasksVMTests
{
    private DateTimeOffset _now = DateTimeOffset.Now;

    private TasksVM Create(TaskTracker tracker) => new(tracker, () => _now);

    [Fact]
    public Task QuickTaskIsNeverShown() => TestUiThread.RunAsync(() =>
    {
        var tracker = new TaskTracker();
        var tasks = Create(tracker);

        var task = tracker.Begin(TrackedTaskKind.Upload, TrackedTaskStage.Uploading, "a.png");
        tasks.Tick();
        Assert.False(tasks.HasItems);

        task.Complete();
        _now += TimeSpan.FromSeconds(2);
        tasks.Tick();
        Assert.False(tasks.HasItems);
    });

    [Fact]
    public Task LongTaskIsShownAndLeavesAfterItIsDone() => TestUiThread.RunAsync(() =>
    {
        var tracker = new TaskTracker();
        var tasks = Create(tracker);
        var task = tracker.Begin(TrackedTaskKind.Upload, TrackedTaskStage.Uploading, "a.png", "chat");
        task.Report(0.5);

        _now += TasksVM.ShowAfter;
        tasks.Tick();
        var item = Assert.Single(tasks.Items);
        Assert.Equal("a.png", item.Title);
        Assert.Equal(1, tasks.RunningCount);
        Assert.Equal(0.5, tasks.Progress);
        Assert.Equal("1 · 50%", tasks.SummaryText);

        task.Complete();
        tasks.Tick();
        Assert.True(item.IsEnded);
        Assert.False(tasks.IsRunning);

        _now += TasksVM.FinishedLinger;
        tasks.Tick();
        Assert.False(tasks.HasItems);
    });

    [Fact]
    public Task FailureStaysUntilDismissed() => TestUiThread.RunAsync(() =>
    {
        var tracker = new TaskTracker();
        var tasks = Create(tracker);
        var task = tracker.Begin(TrackedTaskKind.Download, TrackedTaskStage.Downloading, "a.zip");

        // Even a quick failure is shown
        task.Fail("gone");
        tasks.Tick();
        var item = Assert.Single(tasks.Items);
        Assert.Equal("gone", item.StatusText);
        Assert.True(tasks.HasFailed);

        _now += TimeSpan.FromMinutes(1);
        tasks.Tick();
        Assert.Single(tasks.Items);

        item.DismissCommand.Execute().Subscribe();
        Assert.False(tasks.HasItems);
    });

    [Fact]
    public Task UnmeasuredTasksShowTheirCountAlone() => TestUiThread.RunAsync(() =>
    {
        var tracker = new TaskTracker();
        var tasks = Create(tracker);
        tracker.Begin(TrackedTaskKind.Upload, TrackedTaskStage.Waiting, "a.mp4");
        tracker.Begin(TrackedTaskKind.Compression, TrackedTaskStage.Compressing, "a.mp4");

        _now += TasksVM.ShowAfter;
        tasks.Tick();

        Assert.True(tasks.IsIndeterminate);
        Assert.Equal("2", tasks.SummaryText);
    });

    [Fact]
    public Task CancelFromTheListAsksTheWork() => TestUiThread.RunAsync(() =>
    {
        var tracker = new TaskTracker();
        var tasks = Create(tracker);
        var asked = false;
        tracker.Begin(TrackedTaskKind.Upload, TrackedTaskStage.Uploading, "a.png", cancel: () => asked = true);

        _now += TasksVM.ShowAfter;
        tasks.Tick();
        tasks.Items[0].CancelCommand.Execute().Subscribe();

        Assert.True(asked);
    });
}
