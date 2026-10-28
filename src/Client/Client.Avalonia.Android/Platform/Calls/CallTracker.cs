using System;
using System.Reactive.Linq;
using Avalonia.Threading;
using Client.Services;
using Client.ViewModels.Main;
using Client.ViewModels.Sessions.Hybrid;

namespace Client.Droid.Platform.Calls;

/// <summary>
/// Runs <see cref="CallService"/> while the user is in a voice session, and hands the buttons of its
/// notification to the session.
/// </summary>
internal sealed class CallTracker : IDisposable
{
    private readonly MainWindowVM _viewModel;
    private readonly IDisposable _subscription;

    public CallTracker(MainWindowVM viewModel)
    {
        _viewModel = viewModel;

        _subscription = viewModel.WatchProperty(nameof(MainWindowVM.HybridSession), vm => vm.HybridSession)
            .Select(session => session == null ? Observable.Return<CallState?>(null) : Watch(session))
            .Switch()
            .Subscribe(state =>
            {
                if (state == null)
                    CallService.Hide();
                else
                    CallService.Show(state);
            });

        CallService.ActionRequested += OnActionRequested;
    }

    public void Dispose()
    {
        CallService.ActionRequested -= OnActionRequested;
        _subscription.Dispose();
        CallService.Hide();
    }

    private static IObservable<CallState?> Watch(ActiveHybridSessionVM session)
    {
        var since = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var statusBar = session.StatusBar;

        return statusBar.WatchProperties(nameof(HybridSessionStatusBarVM.SessionName),
                nameof(HybridSessionStatusBarVM.IsMicrophoneOn), nameof(HybridSessionStatusBarVM.IsSoundOn))
            .Select(bar => (CallState?)new CallState(bar.SessionName, bar.IsMicrophoneOn, bar.IsSoundOn, since))
            .DistinctUntilChanged();
    }

    private void OnActionRequested(CallAction action) => Dispatcher.UIThread.Post(() =>
    {
        if (_viewModel.HybridSession?.StatusBar is not { } statusBar)
            return;

        var command = action == CallAction.Leave ? statusBar.CloseCommand : statusBar.ToggleMicrophoneCommand;
        // A command that cannot run now, the microphone while the connection is lost, does nothing
        command.Execute().Subscribe(_ => { }, _ => { });
    });
}
