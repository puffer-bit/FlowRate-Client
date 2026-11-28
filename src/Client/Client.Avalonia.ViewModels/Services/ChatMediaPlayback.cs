using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Application.Coordinator;
using Client.Core.Coordinator;
using Client.ViewModels.Sessions.Chat;

namespace Client.Services;

public interface IChatMediaSession
{
    event Action? Changed;
    double Volume { get; }
    bool IsLoading { get; }
    bool IsPlaying { get; }
    bool HasError { get; }
    double Position { get; }
    double Duration { get; }
    Task ToggleAsync(ChatFileVM file);
    Task SetVolumeAsync(double volume);
    void Seek(ChatFileVM file, double position);
    void Stop();
}

/// <summary>One active chat attachment across media types, chats and virtualized cards. UI thread only.</summary>
public sealed class ChatMediaPlayback : IDisposable
{
    public event Action? Changed;
    public IChatMediaSession? Current { get; private set; }
    public ChatFileVM? File { get; private set; }
    private ICoordinatorSession? _coordinator;
    private Action<CoordinatorState>? _stateChanged;
    private long _generation;

    public bool Begin(IChatMediaSession session, ChatFileVM file)
    {
        if (!Client.Core.Media.MediaRuntime.IsEnabled)
            return false;

        Stop();
        var generation = ++_generation;
        var coordinator = file.CoordinatorSession;
        Current = session;
        File = file;
        _coordinator = coordinator;
        session.Changed += Notify;
        _stateChanged = state =>
        {
            if (state == CoordinatorState.Connected)
                return;
            // Capture the attachment too: a queued disconnect must not close a newer playback.
            Dispatcher.UIThread.Post(() =>
            {
                if (_generation == generation)
                    Stop();
            });
        };
        coordinator.StateChanged += _stateChanged;
        if (coordinator.ConnectionStatus != CoordinatorState.Connected)
        {
            End(session);
            return false;
        }
        Notify();
        return true;
    }

    public void End(IChatMediaSession session)
    {
        if (!ReferenceEquals(Current, session))
            return;
        _generation++;
        session.Changed -= Notify;
        if (_coordinator != null && _stateChanged != null)
            _coordinator.StateChanged -= _stateChanged;
        _coordinator = null;
        _stateChanged = null;
        Current = null;
        File = null;
        Notify();
    }

    public void Stop() => Current?.Stop();

    public async Task ResetAsync()
    {
        var session = Current;
        var file = File;
        if (session == null || file == null)
            return;
        if (session.IsLoading || session.HasError)
        {
            Stop();
            return;
        }
        if (session.IsPlaying)
            await session.ToggleAsync(file);
        if (ReferenceEquals(Current, session) && ReferenceEquals(File, file))
            session.Seek(file, 0);
    }

    private void Notify() => Changed?.Invoke();
    public void Dispose() => Stop();
}
