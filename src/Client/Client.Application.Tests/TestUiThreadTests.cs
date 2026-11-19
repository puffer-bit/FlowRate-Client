using Avalonia.Threading;
using Xunit;

namespace Client.Application.Tests;

public class TestUiThreadTests
{
    [Fact]
    public Task ApplicationAndDispatcherAreReadyOnTheSharedThread() => TestUiThread.RunAsync(() =>
    {
        Assert.NotNull(global::Avalonia.Application.Current);
        Assert.Same(Dispatcher.CurrentDispatcher, Dispatcher.UIThread);
        Assert.True(Dispatcher.UIThread.CheckAccess());
        var executed = false;
        Dispatcher.UIThread.Post(() => executed = true);
        Dispatcher.UIThread.RunJobs();
        Assert.True(executed);
    });
}
