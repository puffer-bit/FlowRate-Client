using Client.Application.Tasks;
using Xunit;

namespace Client.Application.Tests;

public class TaskTrackerTests
{
    [Fact]
    public void BegunTaskIsRunningUntilCompleted()
    {
        var tracker = new TaskTracker();
        ITrackedTask? started = null, ended = null;
        tracker.Started += task => started = task;
        tracker.Ended += task => ended = task;

        var task = tracker.Begin(TrackedTaskKind.Upload, TrackedTaskStage.Uploading, "a.png", "chat");
        Assert.Same(task, started);
        Assert.Equal([task], tracker.Running);
        Assert.Null(task.Progress);

        task.Report(0.4);
        Assert.Equal(0.4, task.Progress);

        task.Complete();
        Assert.Same(task, ended);
        Assert.Empty(tracker.Running);
        Assert.Equal(TrackedTaskState.Completed, task.State);
        Assert.Equal(1, task.Progress);
    }

    [Fact]
    public void TaskEndsOnlyOnce()
    {
        var tracker = new TaskTracker();
        var endings = 0;
        tracker.Ended += _ => endings++;

        var task = tracker.Begin(TrackedTaskKind.Download, TrackedTaskStage.Downloading, "a.zip");
        task.Fail("no space");
        task.Complete();
        task.Dispose();

        Assert.Equal(1, endings);
        Assert.Equal(TrackedTaskState.Failed, task.State);
        Assert.Equal("no space", task.Error);
    }

    [Fact]
    public void DisposingARunningTaskCancelsIt()
    {
        var tracker = new TaskTracker();
        using (tracker.Begin(TrackedTaskKind.Upload, TrackedTaskStage.Uploading, "a.png"))
        {
        }

        Assert.Empty(tracker.Running);
    }

    [Fact]
    public void CancelAsksTheWorkAndLeavesTheEndToIt()
    {
        var tracker = new TaskTracker();
        var asked = 0;
        var task = tracker.Begin(TrackedTaskKind.Upload, TrackedTaskStage.Uploading, "a.png", cancel: () => asked++);
        Assert.True(task.CanCancel);

        task.Cancel();
        Assert.Equal(1, asked);
        Assert.Equal(TrackedTaskState.Running, task.State);

        task.Dispose();
        Assert.False(task.CanCancel);
        task.Cancel();
        Assert.Equal(1, asked);
    }

    [Fact]
    public void NewStageForgetsTheShareOfTheLastOne()
    {
        var tracker = new TaskTracker();
        var task = tracker.Begin(TrackedTaskKind.Upload, TrackedTaskStage.Waiting, "a.mp4");
        task.Report(0.7);

        task.SetStage(TrackedTaskStage.Uploading);

        Assert.Equal(TrackedTaskStage.Uploading, task.Stage);
        Assert.Null(task.Progress);
    }

    [Fact]
    public void EndedTaskIgnoresReports()
    {
        var tracker = new TaskTracker();
        var task = tracker.Begin(TrackedTaskKind.Upload, TrackedTaskStage.Uploading, "a.png");
        task.Report(0.3);
        task.Dispose();

        task.Report(0.9);
        task.SetStage(TrackedTaskStage.Saving);

        Assert.Equal(0.3, task.Progress);
        Assert.Equal(TrackedTaskStage.Uploading, task.Stage);
    }
}
