using System;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Serilog;

namespace Client.Droid.Platform.Calls;

/// <summary>
/// Keeps the process and the microphone alive while the user is in a voice session and the application
/// is off the screen, with an ongoing notification of the session. Without it the system stops recording
/// the moment the application leaves the screen and kills the process soon after.
/// </summary>
/// <remarks>
/// A service of the microphone may only be started while the application is on the screen, so it is
/// started when the session is entered and lives until it is left. Everything static is touched on the
/// main thread only.
/// </remarks>
[Service(Exported = false,
    ForegroundServiceType = ForegroundService.TypeMicrophone | ForegroundService.TypeMediaPlayback)]
internal sealed class CallService : Service
{
    private const int NotificationId = 0x464C5257;

    private static readonly Handler MainThread = new(Looper.MainLooper!);

    // What the notification shows; null once the session is left
    private static CallState? _state;
    private static CallService? _current;
    private static bool _isStarting;

    private PowerManager.WakeLock? _wakeLock;
    private bool _hasMicrophone;

    /// <summary>
    /// Raised on the main thread when a button of the notification is tapped.
    /// </summary>
    public static event Action<CallAction>? ActionRequested;

    /// <summary>
    /// Starts the service, or updates its notification. Called from any thread.
    /// </summary>
    public static void Show(CallState state) => MainThread.Post(() =>
    {
        _state = state;

        if (_current != null)
            _current.Update();
        else if (!_isStarting)
            Start();
    });

    /// <summary>
    /// Stops the service once the session is left. Called from any thread.
    /// </summary>
    public static void Hide() => MainThread.Post(() =>
    {
        _state = null;

        // A service that is still starting stops itself once it has started
        _current?.StopSelf();
    });

    /// <summary>
    /// Declares the microphone to the system once the user has allowed it: the service is started
    /// before the permission is asked for, and a service that has not declared it is not let record
    /// off the screen. Called from any thread, while the application is on the screen.
    /// </summary>
    public static void OnMicrophoneAllowed() => MainThread.Post(() =>
    {
        if (_current is { _hasMicrophone: false } current)
            current.Promote();
    });

    internal static void RaiseAction(CallAction action) => ActionRequested?.Invoke(action);

    private static void Start()
    {
        var context = global::Android.App.Application.Context;

        try
        {
            _isStarting = true;
            ContextCompat.StartForegroundService(context, new Intent(context, typeof(CallService)));
        }
        catch (Exception exception)
        {
            // Off the screen the system refuses; the next change of the session tries again
            _isStarting = false;
            Log.Warning(exception, "Failed to start the call service");
        }
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        _isStarting = false;
        _current = this;

        // Before anything else: the system ends the application that started a service like this one
        // and did not bring it to the front in time
        Promote();

        if (_state == null)
        {
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        if (_wakeLock == null && GetSystemService(PowerService) is PowerManager power)
        {
            // The voice comes in over the network while the screen is off
            _wakeLock = power.NewWakeLock(WakeLockFlags.Partial, "FlowRate:call");
            _wakeLock?.SetReferenceCounted(false);
            _wakeLock?.Acquire();
        }

        // A process killed during the session has nothing to come back to
        return StartCommandResult.NotSticky;
    }

    public override void OnDestroy()
    {
        if (ReferenceEquals(_current, this))
            _current = null;

        if (_wakeLock is { IsHeld: true })
            _wakeLock.Release();
        _wakeLock = null;

        base.OnDestroy();
    }

    private void Promote()
    {
        var notification = CallNotification.Build(this, _state ?? CallState.Leaving);

        if (!OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            StartForeground(NotificationId, notification);
            _hasMicrophone = true;
            return;
        }

        var hasMicrophone = ContextCompat.CheckSelfPermission(this, Manifest.Permission.RecordAudio) == Permission.Granted;

        try
        {
            StartForeground(NotificationId, notification, GetTypes(hasMicrophone));
            _hasMicrophone = hasMicrophone;
        }
        catch (Exception exception) when (hasMicrophone)
        {
            // Off the screen the microphone is refused; the sound of the session is still played
            Log.Warning(exception, "The call service is started without the microphone");
            StartForeground(NotificationId, notification, GetTypes(false));
            _hasMicrophone = false;
        }
    }

    private void Update()
    {
        if (_state is { } state)
            NotificationManagerCompat.From(this)?.Notify(NotificationId, CallNotification.Build(this, state));
    }

    private static ForegroundService GetTypes(bool hasMicrophone) => hasMicrophone
        ? ForegroundService.TypeMicrophone | ForegroundService.TypeMediaPlayback
        : ForegroundService.TypeMediaPlayback;
}
