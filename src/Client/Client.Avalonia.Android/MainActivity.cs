using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Avalonia.Android;
using Client.Droid.Platform.Notifications;
using Client.Droid.Platform;
using Client.Tools.AppInfo;
using Firebase;

namespace Client.Droid;

[Activity(
    Label = "FlowRate",
    Theme = "@style/AppTheme",
    MainLauncher = true,
    // A tapped notification comes to the activity that is already open instead of starting another
    LaunchMode = LaunchMode.SingleTop,
    // Never panned by the system when the keyboard opens: the root view makes room for it itself
    WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    private const int NotificationPermissionRequest = 1001;
    // A request of the media by the permission it asks for
    private static readonly Dictionary<string, int> MediaPermissionRequests = new()
    {
        [Manifest.Permission.RecordAudio] = 7001,
        [Manifest.Permission.Camera] = 7002
    };

    // Of the process, not of the activity: a request survives the activity recreated under its
    // dialog, and its answer comes to the new one. By request code
    private static readonly Dictionary<int, TaskCompletionSource<bool>> _mediaPermissions = [];

    internal Task<bool> RequestMicrophonePermissionAsync() => RequestPermissionAsync(Manifest.Permission.RecordAudio);

    /// <summary>Asks for the microphone or the camera, or answers at once where it is allowed already.</summary>
    internal Task<bool> RequestPermissionAsync(string permission)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestCode = MediaPermissionRequests[permission];

        RunOnUiThread(() =>
        {
            if (!ReferenceEquals(Current, this))
            {
                completion.TrySetResult(false);
                return;
            }

            if (ContextCompat.CheckSelfPermission(this, permission) == Permission.Granted)
            {
                completion.TrySetResult(true);
                return;
            }

            if (_mediaPermissions.TryGetValue(requestCode, out var pending))
            {
                ForwardPermissionAsync(pending.Task, completion);
                return;
            }

            _mediaPermissions[requestCode] = completion;
            ActivityCompat.RequestPermissions(this, [permission], requestCode);
        });

        return completion.Task;
    }

    private static async void ForwardPermissionAsync(Task<bool> pending, TaskCompletionSource<bool> completion) =>
        completion.TrySetResult(await pending);

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        if (!_mediaPermissions.Remove(requestCode, out var pending))
            return;

        pending.TrySetResult(grantResults.Length > 0 && grantResults[0] == Permission.Granted);
    }

    /// <summary>
    /// The activity that shows the application, for the root view to close it once the user has agreed to.
    /// </summary>
    public static MainActivity? Current { get; private set; }

    /// <summary>
    /// Whether the user sees the application.
    /// </summary>
    public static bool IsInFront { get; private set; }

    /// <summary>
    /// Raised on the UI thread when <see cref="IsInFront"/> changes.
    /// </summary>
    public static event Action<bool>? IsInFrontChanged;

    /// <summary>
    /// The chat of the notification the user tapped, until the root view has shown it.
    /// </summary>
    public static (string CoordinatorInstanceId, int ChatId)? RequestedChat { get; set; }

    /// <summary>
    /// Raised on the UI thread when <see cref="RequestedChat"/> is set.
    /// </summary>
    public static event Action? ChatRequested;

    public static (string CoordinatorId, int ChatId, Guid CallId, int UserId, string Action)? RequestedCall { get; set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Current = this;
        // The list of sessions names the phone; the machine name of Android is "localhost"
        DeviceInformation.Name = $"{Build.Manufacturer} {Build.Model}".Trim();
        base.OnCreate(savedInstanceState);

        FirebaseApp.InitializeApp(this);
        ChatNotifications.EnsureChannel(this);

        // Before Android 13 notifications need no permission
        if (OperatingSystem.IsAndroidVersionAtLeast(33)
            && ContextCompat.CheckSelfPermission(this, Manifest.Permission.PostNotifications) != Permission.Granted)
            ActivityCompat.RequestPermissions(this, [Manifest.Permission.PostNotifications], NotificationPermissionRequest);

        // Created again by the system, the activity gets the intent it was first started with once more
        if (savedInstanceState == null)
        {
            ShowOverLockScreenForCall(Intent);
            RequestChat(Intent);
        }
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        ShowOverLockScreenForCall(intent);
        RequestChat(intent);
    }

    /// <summary>
    /// A call accepted on the lock screen goes on over it, as a phone call does.
    /// </summary>
    private void ShowOverLockScreenForCall(Intent? intent)
    {
        if (IncomingCallNotifications.ReadAction(this, intent) is { Action: "accept" })
            SetOverLockScreen(true);
    }

    private void SetOverLockScreen(bool value)
    {
        if (_isOverLockScreen == value)
            return;
        _isOverLockScreen = value;
        if (OperatingSystem.IsAndroidVersionAtLeast(27))
        {
            SetShowWhenLocked(value);
            SetTurnScreenOn(value);
        }
        else if (value)
            Window?.AddFlags(WindowManagerFlags.ShowWhenLocked | WindowManagerFlags.TurnScreenOn);
        else
            Window?.ClearFlags(WindowManagerFlags.ShowWhenLocked | WindowManagerFlags.TurnScreenOn);
    }

    private bool _isOverLockScreen;

    protected override void OnStart()
    {
        base.OnStart();
        SetIsInFront(true);
    }

    protected override void OnStop()
    {
        NativeTextMenu.TryDismissActive();
        // Once the user has left, the lock screen protects the application again
        SetOverLockScreen(false);
        SetIsInFront(false);
        base.OnStop();
    }

    protected override void OnDestroy()
    {
        // A recreated activity still gets the answer; one closed for good never will
        if (IsFinishing && !IsChangingConfigurations)
        {
            foreach (var pending in _mediaPermissions.Values)
                pending.TrySetResult(false);

            _mediaPermissions.Clear();
        }

        if (ReferenceEquals(Current, this))
            Current = null;

        base.OnDestroy();
    }

    private static void RequestChat(Intent? intent)
    {
        if (ChatNotifications.ReadTarget(intent) is not { } chat)
            return;

        if (IncomingCallNotifications.ReadAction(global::Android.App.Application.Context, intent) is { } call)
            RequestedCall = (chat.CoordinatorInstanceId, chat.ChatId, call.CallId, call.UserId, call.Action);
        if (Guid.TryParse(intent?.GetStringExtra(IncomingCallNotifications.CallExtra), out _))
            IncomingCallNotifications.Clear(global::Android.App.Application.Context, chat.CoordinatorInstanceId);
        RequestedChat = chat;
        ChatRequested?.Invoke();
    }

    private static void SetIsInFront(bool isInFront)
    {
        if (IsInFront == isInFront)
            return;

        IsInFront = isInFront;
        IsInFrontChanged?.Invoke(isInFront);
    }
}
