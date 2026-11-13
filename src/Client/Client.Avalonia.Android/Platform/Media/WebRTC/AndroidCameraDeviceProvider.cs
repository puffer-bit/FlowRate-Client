using System;
using System.Collections.Generic;
using System.Linq;
using Client.Application.Ports;
using Client.Avalonia.Localization;
using Com.Flowrate.Rtc;

namespace Client.Droid.Platform.Media.WebRTC;

/// <summary>
/// The front and the back camera of the phone, the front one first: the camera starts facing the user,
/// and switching goes to the other side. See CameraCapture.getDevices.
/// </summary>
internal sealed class AndroidCameraDeviceProvider : ICameraDeviceProvider
{
    public IReadOnlyList<CameraDevice> GetDevices()
    {
        var context = global::Android.App.Application.Context;

        try
        {
            return (CameraCapture.GetDevices(context) ?? [])
                .Select(id => new CameraDevice(id, Strings.Get(CameraCapture.IsFrontFacing(context, id)
                    ? "camera.front"
                    : "camera.back")))
                .ToList();
        }
        catch (Exception)
        {
            // No camera service, or it failed: there is nothing to choose
            return [];
        }
    }
}
