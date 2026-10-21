using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Client.Services;

/// <summary>
/// Runs view model code on the UI thread, which every bound property has to be changed on.
/// </summary>
public static class UiThread
{
    /// <summary>
    /// Runs the action at once when already on the UI thread, otherwise waits for it to run there.
    /// </summary>
    public static Task RunAsync(Action action)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return Dispatcher.UIThread.InvokeAsync(action).GetTask();

        action();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs the asynchronous function on the UI thread and waits for it to end.
    /// </summary>
    public static Task RunAsync(Func<Task> action) =>
        Dispatcher.UIThread.CheckAccess() ? action() : Dispatcher.UIThread.InvokeAsync(action);
}
