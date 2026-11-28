namespace Client.Application.Tasks;

public sealed class TaskTracker : ITaskTracker
{
    private readonly object _lock = new();
    private readonly List<TrackedTask> _running = [];
    private int _lastId;

    public event Action<ITrackedTask>? Started;
    public event Action<ITrackedTask>? Ended;

    public IReadOnlyList<ITrackedTask> Running
    {
        get
        {
            lock (_lock)
                return _running.ToArray();
        }
    }

    public ITrackedTask Begin(TrackedTaskKind kind, TrackedTaskStage stage, string title, string? context = null,
        Action? cancel = null, long? totalBytes = null)
    {
        var task = new TrackedTask(this, Interlocked.Increment(ref _lastId), kind, stage, title, context, cancel, totalBytes);
        lock (_lock)
            _running.Add(task);
        Started?.Invoke(task);
        return task;
    }

    private void End(TrackedTask task)
    {
        lock (_lock)
            _running.Remove(task);
        Ended?.Invoke(task);
    }

    private sealed class TrackedTask(TaskTracker tracker, int id, TrackedTaskKind kind, TrackedTaskStage stage,
        string title, string? context, Action? cancel, long? totalBytes) : ITrackedTask
    {
        private readonly object _lock = new();
        private Action? _cancel = cancel;

        public int Id => id;
        public TrackedTaskKind Kind => kind;
        public string Title
        {
            get { lock (_lock) return field; }
            private set { lock (_lock) field = value; }
        } = title;
        public string? Context => context;
        public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;

        public TrackedTaskStage Stage
        {
            get { lock (_lock) return field; }
            private set { lock (_lock) field = value; }
        } = stage;

        public double? Progress
        {
            get { lock (_lock) return field; }
            private set { lock (_lock) field = value; }
        }

        public long? TotalBytes
        {
            get { lock (_lock) return field; }
            private set { lock (_lock) field = value; }
        } = totalBytes;

        public TrackedTaskState State
        {
            get { lock (_lock) return field; }
            private set { lock (_lock) field = value; }
        } = TrackedTaskState.Running;

        public string? Error
        {
            get { lock (_lock) return field; }
            private set { lock (_lock) field = value; }
        }

        public bool CanCancel
        {
            get { lock (_lock) return _cancel != null && State == TrackedTaskState.Running; }
        }

        public void Report(double share)
        {
            if (State == TrackedTaskState.Running)
                Progress = Math.Clamp(share, 0, 1);
        }

        public void SetStage(TrackedTaskStage value)
        {
            if (State != TrackedTaskState.Running || Stage == value)
                return;
            // The share of the earlier stage says nothing about the new one
            Stage = value;
            Progress = null;
        }

        public void SetTotalBytes(long? bytes) => TotalBytes = bytes;

        public void SetTitle(string value)
        {
            if (State == TrackedTaskState.Running)
                Title = value;
        }

        public void Complete() => TryEnd(TrackedTaskState.Completed, null);

        public void Fail(string? reason) => TryEnd(TrackedTaskState.Failed, reason);

        public void Cancel()
        {
            Action? cancelWork;
            lock (_lock)
                cancelWork = State == TrackedTaskState.Running ? _cancel : null;
            // The work ends the task itself once it has stopped
            cancelWork?.Invoke();
        }

        public void Dispose() => TryEnd(TrackedTaskState.Canceled, null);

        private bool TryEnd(TrackedTaskState state, string? error)
        {
            lock (_lock)
            {
                if (State != TrackedTaskState.Running)
                    return false;
                State = state;
                Error = error;
                if (state == TrackedTaskState.Completed)
                    Progress = 1;
                // The work is done with; the tracker must not keep what the cancel holds alive
                _cancel = null;
            }
            tracker.End(this);
            return true;
        }
    }
}
