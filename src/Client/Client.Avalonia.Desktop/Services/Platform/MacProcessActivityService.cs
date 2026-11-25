using System;
using System.Reactive.Disposables;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Client.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Client.Services.Platform;

/// <summary>
/// Holds one NSProcessInfo activity while any voice lease is alive, so App Nap leaves the
/// process alone; idle system sleep stays allowed.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed partial class MacProcessActivityService(ILogger<MacProcessActivityService> logger)
    : IProcessActivityService
{
    // NSActivityUserInitiatedAllowingIdleSystemSleep | NSActivityLatencyCritical
    private const ulong Options = 0x00EFFFFFUL | 0xFF00000000UL;
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private readonly object _lock = new();
    private int _leases;
    private nint _activity;

    public IDisposable BeginVoiceActivity()
    {
        lock (_lock)
        {
            if (_leases++ == 0)
                Begin();
        }
        return Disposable.Create(() =>
        {
            lock (_lock)
            {
                if (--_leases == 0)
                    End();
            }
        });
    }

    private void Begin()
    {
        var pool = objc_autoreleasePoolPush();
        var reason = CFStringCreateWithCString(0, "FlowRate voice session", 0x08000100);
        try
        {
            var processInfo = Send(objc_getClass("NSProcessInfo"), sel_registerName("processInfo"));
            var activity = Send(processInfo, sel_registerName("beginActivityWithOptions:reason:"), Options, reason);
            if (activity == 0)
            {
                logger.LogWarning("Could not begin the voice activity; App Nap may throttle the voice");
                return;
            }
            // The token comes autoreleased, and ending the activity needs it alive
            _activity = Send(activity, sel_registerName("retain"));
            logger.LogInformation("Voice activity begun, App Nap is held off");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not begin the voice activity; App Nap may throttle the voice");
        }
        finally
        {
            if (reason != 0) CFRelease(reason);
            objc_autoreleasePoolPop(pool);
        }
    }

    private void End()
    {
        if (_activity == 0)
            return;
        var activity = _activity;
        _activity = 0;
        try
        {
            var processInfo = Send(objc_getClass("NSProcessInfo"), sel_registerName("processInfo"));
            SendVoid(processInfo, sel_registerName("endActivity:"), activity);
            SendVoid(activity, sel_registerName("release"));
            logger.LogInformation("Voice activity ended");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not end the voice activity");
        }
    }

    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_getClass(string name);
    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint sel_registerName(string name);
    [LibraryImport(ObjC)]
    private static partial nint objc_autoreleasePoolPush();
    [LibraryImport(ObjC)]
    private static partial void objc_autoreleasePoolPop(nint pool);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector, ulong options, nint reason);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendVoid(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendVoid(nint receiver, nint selector, nint argument);
    [LibraryImport(CoreFoundation, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint CFStringCreateWithCString(nint allocator, string text, uint encoding);
    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(nint value);
}
