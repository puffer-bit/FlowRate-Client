using System.Windows.Input;
using Xunit;

namespace Client.Application.Tests;

/// <summary>Waits for command state that ReactiveUI delivers on the task pool in tests.</summary>
internal static class CommandWait
{
    /// <summary>
    /// Waits until the command can execute again. The end of an execution reaches the command on
    /// <c>RxSchedulers.MainThreadScheduler</c>, the task pool in tests, which a busy test run may delay.
    /// </summary>
    public static async Task UntilCanExecuteAsync(ICommand command, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (!command.CanExecute(null))
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The command did not become executable.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
