namespace Client.Application.Tasks;

/// <summary>What a tracked task does, for its icon.</summary>
public enum TrackedTaskKind
{
    Upload,
    Download,
    Compression,
    /// <summary>Data of the server being loaded, as the list of sessions after a connection.</summary>
    Loading,
    /// <summary>A connection to the server being made or restored.</summary>
    Connection
}

/// <summary>The part of the work a task is in, shown under its name.</summary>
public enum TrackedTaskStage
{
    /// <summary>Waits for something else first, as an upload for its compressed copy.</summary>
    Waiting,
    Compressing,
    Uploading,
    Downloading,
    Saving,
    Loading,
    Connecting,
    /// <summary>Looks for something newer, as a release of the application.</summary>
    Checking
}

public enum TrackedTaskState
{
    Running,
    Completed,
    Failed,
    Canceled
}

/// <summary>
/// A piece of work the user started and may want to follow: an upload, a download, a compression.
/// The work keeps running where it was started; it only tells the tracker how far it got. Any method may
/// be called from any thread.
/// </summary>
public interface ITrackedTask : IDisposable
{
    int Id { get; }
    TrackedTaskKind Kind { get; }

    /// <summary>What the task is about, as a file name.</summary>
    string Title { get; }

    /// <summary>Where it belongs, as the name of a chat; null when nowhere in particular.</summary>
    string? Context { get; }

    DateTimeOffset StartedAt { get; }
    TrackedTaskStage Stage { get; }

    /// <summary>The share done, from 0 to 1; null while it cannot be told.</summary>
    double? Progress { get; }

    /// <summary>The bytes the whole task moves, for its speed; null when unknown.</summary>
    long? TotalBytes { get; }

    TrackedTaskState State { get; }

    /// <summary>Why it failed, ready to be shown.</summary>
    string? Error { get; }

    bool CanCancel { get; }

    void Report(double share);
    void SetStage(TrackedTaskStage stage);
    void SetTotalBytes(long? bytes);

    /// <summary>Tells anew what the task is about, as the attempt of a connection.</summary>
    void SetTitle(string title);
    void Complete();
    void Fail(string? reason);

    /// <summary>Asks the work to stop, as its own cancel button would.</summary>
    void Cancel();
}

/// <summary>
/// The work in progress the user started. A registry, not an executor: it holds no view models and ends
/// nothing by itself. A task leaves it once ended; <see cref="Ended"/> tells how.
/// </summary>
public interface ITaskTracker
{
    /// <summary>
    /// Starts following a piece of work. Disposing the returned task without completing or failing it ends
    /// it as canceled, so a <c>using</c> never leaves one running.
    /// </summary>
    /// <param name="cancel">Stops the work, as its own cancel button; null when it cannot be stopped.</param>
    ITrackedTask Begin(TrackedTaskKind kind, TrackedTaskStage stage, string title, string? context = null,
        Action? cancel = null, long? totalBytes = null);

    /// <summary>The tasks still running.</summary>
    IReadOnlyList<ITrackedTask> Running { get; }

    /// <summary>Raised on the thread that began the task.</summary>
    event Action<ITrackedTask>? Started;

    /// <summary>Raised on the thread that ended the task, once, with its final state.</summary>
    event Action<ITrackedTask>? Ended;
}
