using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Versioning;
using Client.Application.Ports;
using Client.Core.Media;
using Client.GStreamer.Video.Mac;

namespace Client.Services.ScreenCast.Mac;

/// <summary>
/// The displays and the windows ScreenCaptureKit can capture. The handle of a source is its
/// CGDirectDisplayID or CGWindowID.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacScreenSourceProvider : IScreenSourceProvider
{
    public bool RequestAccess() => ScreenCaptureKit.HasAccess || ScreenCaptureKit.RequestAccess();

    public IReadOnlyList<ScreenSource> GetWindows() =>
        ScreenCaptureKit.GetWindows()
            .Select(window => new ScreenSource(window.Title, new IntPtr((long)window.Id), window.ProcessId))
            .ToList();

    public IReadOnlyList<ScreenSource> GetMonitors() =>
        ScreenCaptureKit.GetDisplays()
            .Select(display => new ScreenSource(
                string.Format(CultureInfo.InvariantCulture, "{0} {1}×{2}", display.Title, display.Width, display.Height).Trim(),
                new IntPtr((long)display.Id)))
            .ToList();
}
