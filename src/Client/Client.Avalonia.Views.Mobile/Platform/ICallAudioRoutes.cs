using System;
using System.Collections.Generic;

namespace Client.Mobile.Platform;

public enum CallAudioDeviceKind { Bluetooth, Wired, Usb, Speaker, Earpiece, Other }

public sealed record CallAudioDevice(string Key, int Id, CallAudioDeviceKind Kind, string Name);

/// <param name="PreferredKey">The device the user chose, null for the automatic choice.</param>
public sealed record CallAudioState(IReadOnlyList<CallAudioDevice> Devices, string? PreferredKey,
    CallAudioDevice? ActiveDevice, bool IsInCall, bool IsSwitching, bool HasError);

/// <summary>The devices the sound of a call can go to, and the choice between them.</summary>
public interface ICallAudioRoutes
{
    /// <summary>A snapshot, safe to read from the UI thread.</summary>
    CallAudioState State { get; }

    /// <summary>Raised on any thread when <see cref="State"/> changes.</summary>
    event Action? Changed;

    /// <param name="key">The device to use, null for the automatic choice.</param>
    void Select(string? key);
}
