using System;
using System.Collections.Generic;
using System.Linq;
using Android.Content;
using Android.Media;
using Android.OS;
using Android.Util;

namespace Client.Droid.Platform.Media;

internal sealed record CallAudioState(IReadOnlyList<CallAudioDevice> Devices, string? PreferredKey,
    CallAudioDevice? ActiveDevice, bool IsInCall, bool IsSwitching, bool HasError);

/// <summary>
/// Process-wide call routing, independent of the activity and surviving replacement WebRTC peers.
/// All AudioManager state changes run on Android's main thread. A snapshot is safe to read from the UI.
/// </summary>
internal sealed class AndroidCallAudioRouter
{
    private static readonly Lazy<AndroidCallAudioRouter> Shared = new(() => new());
    public static AndroidCallAudioRouter Instance => Shared.Value;

    private readonly Context _context = global::Android.App.Application.Context;
    private readonly AudioManager _audio;
    private readonly Handler _handler = new(Looper.MainLooper!);
    // Native registrations need strong managed references for the lifetime of the process.
    private readonly DevicesCallback _devicesCallback;
    private readonly CommunicationListener? _communicationListener;
    private readonly ScoReceiver? _scoReceiver;
    private IReadOnlyList<AudioDeviceInfo> _nativeDevices = [];
    private IReadOnlyList<CallAudioDevice> _devices = [];
    private string? _preferredKey;
    private int _users;
    private Mode _previousMode;
    private bool _previousSpeaker;
    private bool _previousSco;
    private bool _startedSco;
    private int? _requestedId;
    private int _requestVersion;
    private bool _hasError;
    private volatile CallAudioState _state = new([], null, null, false, false, false);

    public CallAudioState State => _state;
    public event Action? Changed;

    private AndroidCallAudioRouter()
    {
        _audio = (AudioManager)_context.GetSystemService(Context.AudioService)!;
        _devicesCallback = new DevicesCallback(this);
        _audio.RegisterAudioDeviceCallback(_devicesCallback, _handler);
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            _communicationListener = new CommunicationListener(this);
            _audio.AddOnCommunicationDeviceChangedListener(_context.MainExecutor!, _communicationListener);
        }
        else
        {
            _scoReceiver = new ScoReceiver(this);
            _context.RegisterReceiver(_scoReceiver, new IntentFilter(AudioManager.ActionScoAudioStateUpdated));
        }
        _handler.Post(RefreshDevices);
    }

    public void BeginCall() => _handler.Post(() =>
    {
        if (_users++ != 0)
            return;

        _previousMode = _audio.Mode;
        if (!OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            _previousSpeaker = _audio.SpeakerphoneOn;
            _previousSco = _audio.BluetoothScoOn;
        }
        _requestedId = null;
        _hasError = false;
        _audio.Mode = Mode.InCommunication;
        RefreshDevices();
    });

    public void EndCall() => _handler.Post(() =>
    {
        if (_users == 0 || --_users != 0)
            return;

        ++_requestVersion;
        _requestedId = null;
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
            _audio.ClearCommunicationDevice();
        else
        {
            StopSco();
            _audio.BluetoothScoOn = _previousSco;
            _audio.SpeakerphoneOn = _previousSpeaker;
        }
        _audio.Mode = _previousMode;
        _hasError = false;
        Publish();
    });

    public void Select(string? key) => _handler.Post(() =>
    {
        // A list entry can disappear between opening the list and tapping it.
        RefreshDeviceList();
        _preferredKey = _devices.Any(device => device.Key == key) ? key : null;
        _requestedId = null;
        _hasError = false;
        if (_users > 0)
            ApplyRoute();
        else
            Publish();
    });

    private void RefreshDeviceList()
    {
        _nativeDevices = OperatingSystem.IsAndroidVersionAtLeast(31)
            ? _audio.AvailableCommunicationDevices.ToArray()
            : (_audio.GetDevices(GetDevicesTargets.Outputs) ?? [])
                .Where(device => Kind(device.Type) != CallAudioDeviceKind.Other).ToArray();
        if (!OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            var wired = _nativeDevices.FirstOrDefault(device => Kind(device.Type)
                is CallAudioDeviceKind.Wired or CallAudioDeviceKind.Usb);
            if (wired != null)
                _nativeDevices = _nativeDevices.Where(device =>
                    Kind(device.Type) is not (CallAudioDeviceKind.Earpiece or CallAudioDeviceKind.Wired or CallAudioDeviceKind.Usb)
                    || device.Id == wired.Id).ToArray();
        }
        _devices = _nativeDevices.Select(device => new CallAudioDevice(
            device.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), device.Id,
            Kind(device.Type), device.ProductName?.ToString() ?? string.Empty)).ToArray();
    }

    private void RefreshDevices()
    {
        RefreshDeviceList();
        if (_preferredKey != null && !_devices.Any(device => device.Key == _preferredKey))
            _preferredKey = null;
        if (_users > 0)
            ApplyRoute();
        else
            Publish();
    }

    private void ApplyRoute()
    {
        var target = CallAudioRoutePolicy.Choose(_devices, _preferredKey);
        if (target == null || target.Id == _requestedId)
        {
            Publish();
            return;
        }

        var native = _nativeDevices.First(device => device.Id == target.Id);
        _requestedId = target.Id;
        var version = ++_requestVersion;
        _hasError = false;
        try
        {
            Log.Info("FlowRate.Audio", $"Request call device {native.Type} ({target.Id})");
            if (OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                if (!_audio.SetCommunicationDevice(native))
                {
                    FailRoute(version);
                    return;
                }
            }
            else if (target.Kind == CallAudioDeviceKind.Bluetooth)
            {
                _audio.SpeakerphoneOn = false;
                if (!_audio.BluetoothScoOn)
                {
                    _startedSco = true;
                    _audio.StartBluetoothSco();
                }
            }
            else
            {
                StopSco();
                _audio.BluetoothScoOn = false;
                _audio.SpeakerphoneOn = target.Kind == CallAudioDeviceKind.Speaker;
            }
        }
        catch (Java.Lang.RuntimeException error)
        {
            Log.Warn("FlowRate.Audio", $"Cannot route call: {error.Message}");
            FailRoute(version);
            return;
        }

        Publish();
        // Bluetooth routing is asynchronous. Never report the requested headset as active until
        // AudioManager confirms it; release a request that never completed so audio is not stranded.
        _handler.PostDelayed(() =>
        {
            if (_users > 0 && version == _requestVersion && ActiveDevice()?.Id != target.Id)
                FailRoute(version);
        }, 30000);
    }

    private void FailRoute(int version)
    {
        if (version != _requestVersion)
            return;
        ++_requestVersion;
        _hasError = true;
        Log.Warn("FlowRate.Audio", "Call device switch failed or timed out");
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
            _audio.ClearCommunicationDevice();
        else
            StopSco();
        Publish();
    }

    private void StopSco()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(31) || !_startedSco)
            return;
        _audio.StopBluetoothSco();
        _audio.BluetoothScoOn = false;
        _startedSco = false;
    }

    private CallAudioDevice? ActiveDevice()
    {
        if (_users == 0)
            return null;
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            var activeId = _audio.CommunicationDevice?.Id;
            return _devices.FirstOrDefault(device => device.Id == activeId);
        }
        if (_audio.BluetoothScoOn)
            return _devices.FirstOrDefault(device => device.Kind == CallAudioDeviceKind.Bluetooth);
        if (_audio.SpeakerphoneOn)
            return _devices.FirstOrDefault(device => device.Kind == CallAudioDeviceKind.Speaker);
        return _devices.FirstOrDefault(device => device.Kind is CallAudioDeviceKind.Wired or CallAudioDeviceKind.Usb)
            ?? _devices.FirstOrDefault(device => device.Kind == CallAudioDeviceKind.Earpiece);
    }

    private void Publish()
    {
        var active = ActiveDevice();
        _state = new CallAudioState(_devices, _preferredKey, active, _users > 0,
            _users > 0 && !_hasError && _requestedId != null && active?.Id != _requestedId, _hasError);
        Changed?.Invoke();
    }

    private static CallAudioDeviceKind Kind(AudioDeviceType type)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(31)
            && type is AudioDeviceType.BleHeadset or AudioDeviceType.BleSpeaker)
            return CallAudioDeviceKind.Bluetooth;
        if (OperatingSystem.IsAndroidVersionAtLeast(28) && type == AudioDeviceType.HearingAid)
            return CallAudioDeviceKind.Bluetooth;
        if (OperatingSystem.IsAndroidVersionAtLeast(26) && type == AudioDeviceType.UsbHeadset)
            return CallAudioDeviceKind.Usb;
        if (OperatingSystem.IsAndroidVersionAtLeast(30) && type == AudioDeviceType.BuiltinSpeakerSafe)
            return CallAudioDeviceKind.Speaker;
        return type switch
        {
            AudioDeviceType.BluetoothSco => CallAudioDeviceKind.Bluetooth,
            AudioDeviceType.WiredHeadset or AudioDeviceType.WiredHeadphones => CallAudioDeviceKind.Wired,
            AudioDeviceType.UsbDevice or AudioDeviceType.UsbAccessory => CallAudioDeviceKind.Usb,
            AudioDeviceType.BuiltinSpeaker => CallAudioDeviceKind.Speaker,
            AudioDeviceType.BuiltinEarpiece => CallAudioDeviceKind.Earpiece,
            _ => CallAudioDeviceKind.Other
        };
    }

    private sealed class DevicesCallback(AndroidCallAudioRouter owner) : AudioDeviceCallback
    {
        public override void OnAudioDevicesAdded(AudioDeviceInfo[]? addedDevices) => owner.RefreshDevices();
        public override void OnAudioDevicesRemoved(AudioDeviceInfo[]? removedDevices) => owner.RefreshDevices();
    }

    private sealed class CommunicationListener(AndroidCallAudioRouter owner) : Java.Lang.Object,
        AudioManager.IOnCommunicationDeviceChangedListener
    {
        public void OnCommunicationDeviceChanged(AudioDeviceInfo? device) => owner.Publish();
    }

    private sealed class ScoReceiver(AndroidCallAudioRouter owner) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(31))
                return;
            var state = intent?.GetIntExtra(AudioManager.ExtraScoAudioState, -1);
            if (state == (int)ScoAudioState.Connected && owner._users > 0
                && CallAudioRoutePolicy.Choose(owner._devices, owner._preferredKey)?.Kind == CallAudioDeviceKind.Bluetooth)
                owner._audio.BluetoothScoOn = true;
            else if (state == (int)ScoAudioState.Disconnected || state == (int)ScoAudioState.Error)
                owner._audio.BluetoothScoOn = false;
            owner.Publish();
        }
    }
}
