using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using Avalonia.Threading;
using Client.Application.Settings;
using Client.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Client.Services.HotKeys;

/// <summary>
/// Listens to the keyboard of the whole machine while some hot key is to be heard in other
/// applications, and reports the presses of the assigned keys to <see cref="IHotKeysService"/>.
/// Follows every change of the settings at once.
/// </summary>
public sealed partial class GlobalHotKeys : IDisposable
{
    private readonly IHotKeysService _service;
    private readonly ILogger<GlobalHotKeys> _logger;
    private readonly System.Threading.Lock _lock = new();

    private IGlobalKeyListener? _listener;
    private volatile (HotKeyGesture Gesture, HotKeyAction Action)[] _bindings = [];

    // Touched only by the thread of the listener
    private readonly HashSet<PhysicalKey> _modifiersDown = [];

    public GlobalHotKeys(IHotKeysService service, ILogger<GlobalHotKeys> logger)
    {
        _service = service;
        _logger = logger;
    }

    public void Start()
    {
        _service.SettingsChanged += Update;
        Update();
    }

    private void Update()
    {
        lock (_lock)
        {
            var settings = _service.Current;
            _bindings = settings is { IsEnabled: true, IsGlobalEnabled: true }
                ? settings.Bindings
                    .Where(binding => binding.IsGlobal)
                    .Select(binding => (Parsed: HotKeyGesture.TryParse(binding.Gesture, out var gesture), gesture, binding.Action))
                    .Where(binding => binding.Parsed)
                    .Select(binding => (binding.gesture, binding.Action))
                    .ToArray()
                : [];

            if (_bindings.Length > 0 && _listener == null)
                _listener = CreateListener();
            else if (_bindings.Length == 0 && _listener != null)
            {
                _listener.Dispose();
                _listener = null;
                LogStopped();
            }

            // Raises SettingsChanged again when it changes, and the nested update finds nothing to do
            _service.Backend = _listener?.Kind ?? GlobalHotKeysBackend.None;
        }
    }

    private IGlobalKeyListener? CreateListener()
    {
        IGlobalKeyListener? listener = null;
        try
        {
            if (OperatingSystem.IsLinux())
                listener = EvdevKeyListener.TryStart(OnKey) ?? (IGlobalKeyListener?)XInputKeyListener.TryStart(OnKey, OnModifierSync);
            else if (OperatingSystem.IsWindows())
                listener = WindowsKeyListener.TryStart(OnKey);
        }
        catch (Exception exception)
        {
            LogStartFailed(exception);
        }

        if (listener == null)
            LogUnavailable();
        else
            LogStarted(listener.Kind);
        return listener;
    }

    private void OnKey(PhysicalKey key, bool isDown, bool isRepeat)
    {
        if (!isDown)
        {
            _modifiersDown.Remove(key);
            return;
        }

        if (isRepeat || key == PhysicalKey.None)
            return;

        // The modifiers held before the key; a modifier is a hot key of its own as well
        var modifiers = KeyModifiers.None;
        foreach (var modifier in _modifiersDown)
            modifiers |= HotKeyGesture.ModifierOf(modifier);
        if (HotKeyGesture.IsModifier(key))
            _modifiersDown.Add(key);

        var pressed = HotKeyGesture.Of(modifiers, key);
        foreach (var (gesture, action) in _bindings)
        {
            if (gesture == pressed)
                Dispatcher.UIThread.Post(() => _service.Raise(action));
        }
    }

    /// <summary>The state of a modifier as the system sees it, for a release that never came.</summary>
    private void OnModifierSync(PhysicalKey key, bool isDown)
    {
        if (isDown)
            _modifiersDown.Add(key);
        else
            _modifiersDown.Remove(key);
    }

    public void Dispose()
    {
        _service.SettingsChanged -= Update;
        lock (_lock)
        {
            _listener?.Dispose();
            _listener = null;
        }
    }

    [LoggerMessage(LogLevel.Information, "Hot keys are heard in other applications through {Backend}")]
    private partial void LogStarted(GlobalHotKeysBackend backend);

    [LoggerMessage(LogLevel.Information, "Hot keys are no longer heard in other applications")]
    private partial void LogStopped();

    [LoggerMessage(LogLevel.Warning, "No way to hear hot keys in other applications, they work only in the window")]
    private partial void LogUnavailable();

    [LoggerMessage(LogLevel.Warning, "Failed to start listening to the keyboard")]
    private partial void LogStartFailed(Exception exception);
}
