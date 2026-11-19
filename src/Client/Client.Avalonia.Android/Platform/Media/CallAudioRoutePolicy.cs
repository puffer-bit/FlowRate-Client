using System.Collections.Generic;
using System.Linq;

namespace Client.Droid.Platform.Media;

internal enum CallAudioDeviceKind { Bluetooth, Wired, Usb, Speaker, Earpiece, Other }

internal sealed record CallAudioDevice(string Key, int Id, CallAudioDeviceKind Kind, string Name);

/// <summary>Keep an explicit choice while it is connected; otherwise prefer a headset to the phone.</summary>
internal static class CallAudioRoutePolicy
{
    public static CallAudioDevice? Choose(IReadOnlyList<CallAudioDevice> devices, string? preferredKey) =>
        devices.FirstOrDefault(device => device.Key == preferredKey)
        ?? devices.OrderBy(device => Priority(device.Kind)).ThenBy(device => device.Id).FirstOrDefault();

    private static int Priority(CallAudioDeviceKind kind) => kind switch
    {
        CallAudioDeviceKind.Bluetooth => 0,
        CallAudioDeviceKind.Wired or CallAudioDeviceKind.Usb => 1,
        CallAudioDeviceKind.Speaker => 2,
        CallAudioDeviceKind.Earpiece => 3,
        _ => 4
    };
}
