using System;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Application.Ports;
using Client.Application.Settings;
using Client.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Client.Services;

/// <summary>Combines leases from every session with the saved behavior preference.</summary>
public abstract class ScreenAwakeService : IScreenAwakeService
{
    private readonly IBehaviorService _behavior;
    private readonly ILogger _logger;
    private int _leases;
    private int _settingsVersion;
    private bool _initialized;
    private bool _enabled;
    private bool _required;
    private bool _disposed;

    protected ScreenAwakeService(IBehaviorService behavior, ILogger logger)
    {
        _behavior = behavior;
        _logger = logger;
        behavior.SettingsChanged += OnSettingsChanged;
    }

    public IDisposable Acquire()
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        _leases++;
        if (!_initialized)
        {
            _initialized = true;
            _ = LoadSettingsAsync();
        }
        Update();
        return Disposable.Create(() =>
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_disposed) return;
            _leases--;
            Update();
        });
    }

    private async Task LoadSettingsAsync()
    {
        var version = _settingsVersion;
        try
        {
            var enabled = (await _behavior.GetSettingsAsync()).KeepScreenOnWhileWatching;
            OnUiThread(() =>
            {
                // A save made while loading wins over the older database snapshot.
                if (_disposed || version != _settingsVersion) return;
                _enabled = enabled;
                Update();
            });
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not load the screen awake preference");
        }
    }

    private void OnSettingsChanged(BehaviorSettings settings)
    {
        var enabled = settings.KeepScreenOnWhileWatching;
        OnUiThread(() =>
        {
            if (_disposed) return;
            _settingsVersion++;
            _enabled = enabled;
            Update();
        });
    }

    private void Update()
    {
        var required = !_disposed && _enabled && _leases > 0;
        if (_required == required) return;
        _required = required;
        try { SetRequired(required); }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not change screen awake state to {Required}", required);
        }
    }

    protected abstract void SetRequired(bool required);

    private static void OnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        _behavior.SettingsChanged -= OnSettingsChanged;
        Update();
    }
}
