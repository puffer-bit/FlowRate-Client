using System.Collections.Concurrent;
using Avalonia.Threading;

namespace Client.Application.Tests;

/// <summary>Runs dispatcher-dependent tests on one persistent Avalonia UI thread.</summary>
internal static class TestUiThread
{
    private static readonly BlockingCollection<Action> Actions = new();

    static TestUiThread()
    {
        new Thread(() =>
        {
            _ = Dispatcher.UIThread;
            foreach (var action in Actions.GetConsumingEnumerable())
                action();
        }) { IsBackground = true, Name = "Test UI" }.Start();
    }

    public static Task RunAsync(Action action)
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Actions.Add(() =>
        {
            try { action(); result.SetResult(); }
            catch (Exception exception) { result.SetException(exception); }
        });
        return result.Task.WaitAsync(TimeSpan.FromSeconds(10), Xunit.TestContext.Current.CancellationToken);
    }
}
