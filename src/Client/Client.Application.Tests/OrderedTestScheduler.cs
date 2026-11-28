using System.Collections.Concurrent;
using ReactiveUI;
using ReactiveUI.Primitives.Concurrency;

namespace Client.Application.Tests;

/// <summary>
/// Gives <see cref="RxSchedulers.MainThreadScheduler"/> one ordered thread in tests, as the app's UI
/// dispatcher is. Its default, the task pool, runs queued work in any order: a command could apply the
/// end of an execution before its start and then stay busy for good, which made tests that wait for a
/// command to become executable again time out now and then.
/// </summary>
internal static class OrderedTestScheduler
{
    private static readonly Lazy<ISequencer> Instance = new(() => new SynchronizationContextSequencer(new Context()));

    public static void Install() => RxSchedulers.MainThreadScheduler = Instance.Value;

    private sealed class Context : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        private readonly Thread _thread;

        public Context()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "Test main scheduler" };
            _thread.Start();
        }

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (Thread.CurrentThread == _thread)
            {
                d(state);
                return;
            }
            using var done = new ManualResetEventSlim();
            Exception? error = null;
            Post(_ =>
            {
                try { d(state); }
                catch (Exception exception) { error = exception; }
                finally { done.Set(); }
            }, null);
            done.Wait();
            if (error != null)
                throw error;
        }

        public override SynchronizationContext CreateCopy() => this;

        private void Run()
        {
            SetSynchronizationContext(this);
            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
            {
                // Like the task pool before it: a failing work item does not stop the ones after it
                try { callback(state); }
                catch { }
            }
        }
    }
}
