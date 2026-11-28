using System;
using System.Globalization;
using Client.Application.Tasks;
using Client.Avalonia.Localization;
using Client.Services;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Tasks;

/// <summary>
/// A row of the task list: a copy of the state of its task, taken by <see cref="TasksVM"/> on its timer.
/// </summary>
public sealed class TrackedTaskVM : ReactiveObject
{
    // The speed follows the bytes of the last moments rather than jumping with every report
    private const double SpeedSmoothing = 0.3;

    private double _lastProgress;
    private DateTimeOffset _lastSampleAt;
    private double _bytesPerSecond;

    internal ITrackedTask Task { get; }

    /// <summary>Set once the task ended, for how long it is still shown.</summary>
    internal DateTimeOffset? EndedAt { get; private set; }

    /// <summary>Set once the row started fading out, to be taken off the list after it.</summary>
    internal DateTimeOffset? LeftAt { get; private set; }

    /// <summary>The row fades out before it is taken off the list.</summary>
    public bool IsLeaving
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    internal void Leave(DateTimeOffset now)
    {
        LeftAt ??= now;
        IsLeaving = true;
    }

    public TrackedTaskKind Kind => Task.Kind;
    public bool IsUpload => Kind == TrackedTaskKind.Upload;
    public bool IsDownload => Kind == TrackedTaskKind.Download;
    public bool IsCompression => Kind == TrackedTaskKind.Compression;
    /// <summary>Work with no icon of its own: it spins.</summary>
    public bool IsLoading => Kind is TrackedTaskKind.Loading or TrackedTaskKind.Connection;

    public string Title
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;
    public string? Context => Task.Context;
    public bool HasContext => !string.IsNullOrEmpty(Task.Context);

    public TrackedTaskState State
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsRunning));
            this.RaisePropertyChanged(nameof(IsFailed));
            this.RaisePropertyChanged(nameof(IsEnded));
            this.RaisePropertyChanged(nameof(IsCompleted));
            this.RaisePropertyChanged(nameof(ShowsKindIcon));
        }
    }

    public bool IsRunning => State == TrackedTaskState.Running;
    public bool IsFailed => State == TrackedTaskState.Failed;
    public bool IsEnded => State != TrackedTaskState.Running;
    public bool IsCompleted => State == TrackedTaskState.Completed;

    /// <summary>The icon of the kind of work; a finished task shows a check, a failed one the warning.</summary>
    public bool ShowsKindIcon => State is TrackedTaskState.Running or TrackedTaskState.Canceled;

    /// <summary>The share done, from 0 to 1; 0 while it cannot be told.</summary>
    public double Progress
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsIndeterminate
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>What it is doing, or how it ended.</summary>
    public string StatusText
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    /// <summary>The share and the speed, as "45% · 2.1 MB/s"; empty when neither is known.</summary>
    public string DetailText
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    public bool CanCancel
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> CancelCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DismissCommand { get; }

    internal TrackedTaskVM(ITrackedTask task, DateTimeOffset now, Action<TrackedTaskVM> dismiss)
    {
        Task = task;
        _lastSampleAt = now;
        CancelCommand = ReactiveCommand.Create(task.Cancel);
        DismissCommand = ReactiveCommand.Create(() => dismiss(this));
        Refresh(now);
    }

    /// <summary>Takes the current state of the task. Called on the UI thread.</summary>
    internal void Refresh(DateTimeOffset now)
    {
        var state = Task.State;
        if (state != TrackedTaskState.Running && EndedAt == null)
            EndedAt = now;
        State = state;

        Title = Task.Title;
        var progress = Task.Progress;
        IsIndeterminate = state == TrackedTaskState.Running && progress == null;
        Progress = progress ?? 0;
        CanCancel = Task.CanCancel;
        MeasureSpeed(progress ?? 0, now);

        StatusText = state switch
        {
            TrackedTaskState.Completed => Strings.Get("tasks.done"),
            TrackedTaskState.Canceled => Strings.Get("tasks.canceled"),
            TrackedTaskState.Failed => Task.Error ?? Strings.Get("chat.attach.error.failed"),
            _ => StageText(Task.Stage)
        };
        DetailText = state == TrackedTaskState.Running ? Detail(progress) : string.Empty;
    }

    private void MeasureSpeed(double progress, DateTimeOffset now)
    {
        var elapsed = (now - _lastSampleAt).TotalSeconds;
        if (elapsed < 0.5)
            return;

        // A new stage starts its share over: no speed is told from that
        var moved = progress - _lastProgress;
        if (moved >= 0 && Task.TotalBytes is long total and > 0)
        {
            var current = moved * total / elapsed;
            _bytesPerSecond = _bytesPerSecond == 0 ? current : _bytesPerSecond + SpeedSmoothing * (current - _bytesPerSecond);
        }
        _lastProgress = progress;
        _lastSampleAt = now;
    }

    private string Detail(double? progress)
    {
        if (progress is not { } share)
            return string.Empty;

        var percent = (share * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        // Only moving bytes have a speed; a compression has none to tell
        return Task.Stage is TrackedTaskStage.Uploading or TrackedTaskStage.Downloading && _bytesPerSecond >= 1
            ? $"{percent} · {Strings.Format("tasks.speed", ("size", SizeText.Format((long)_bytesPerSecond)))}"
            : percent;
    }

    private static string StageText(TrackedTaskStage stage) => Strings.Get(stage switch
    {
        TrackedTaskStage.Waiting => "tasks.stage.waiting",
        TrackedTaskStage.Compressing => "tasks.stage.compressing",
        TrackedTaskStage.Uploading => "tasks.stage.uploading",
        TrackedTaskStage.Downloading => "tasks.stage.downloading",
        TrackedTaskStage.Loading => "tasks.stage.loading",
        TrackedTaskStage.Connecting => "tasks.stage.connecting",
        TrackedTaskStage.Checking => "tasks.stage.checking",
        _ => "tasks.stage.saving"
    });
}
