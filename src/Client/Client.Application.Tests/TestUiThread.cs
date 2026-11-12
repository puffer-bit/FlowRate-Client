using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Threading;
using Xunit;

[assembly: AssemblyFixture(typeof(Client.Application.Tests.TestUiThreadFixture))]

namespace Client.Application.Tests;

/// <summary>Runs dispatcher-dependent tests on one persistent Avalonia UI thread.</summary>
internal static class TestUiThread
{
    private static readonly BlockingCollection<Action> Actions = new();
    private static readonly TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    static TestUiThread()
    {
        new Thread(() =>
        {
            try
            {
                AppBuilder.Configure<global::Avalonia.Application>()
                    .UseStandardRuntimePlatformSubsystem()
                    .UseWindowingSubsystem(() => { }, "Tests")
                    .UseRenderingSubsystem(global::Avalonia.Skia.SkiaPlatform.Initialize, "Skia")
                    .UseTextShapingSubsystem(() => { }, "Tests")
                    .SetupWithoutStarting();
                Dispatcher.UIThread.VerifyAccess();
                Ready.SetResult();
            }
            catch (Exception exception)
            {
                Ready.SetException(exception);
                return;
            }
            foreach (var action in Actions.GetConsumingEnumerable())
                action();
        }) { IsBackground = true, Name = "Test UI" }.Start();
    }

    // Complete startup before any test can create a dispatcher on a worker thread.
    public static void Initialize() => Ready.Task.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();

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

public sealed class TestUiThreadFixture
{
    public TestUiThreadFixture() => TestUiThread.Initialize();
}
