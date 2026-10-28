using System;
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
    private const int MicrophonePermissionRequest = 7001;
    // Of the process, not of the activity: a request survives the activity recreated under its
    // dialog, and its answer comes to the new one
    private static TaskCompletionSource<bool>? _microphonePermission;

    internal Task<bool> RequestMicrophonePermissionAsync()
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        RunOnUiThread(() =>
        {
            if (!ReferenceEquals(Current, this))
            {
                completion.TrySetResult(false);
                return;
            }

            if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.RecordAudio) == Permission.Granted)
            {
                completion.TrySetResult(true);
                return;
            }

            if (_microphonePermission is { } pending)
            {
                ForwardPermissionAsync(pending.Task, completion);
                return;
            }

            _microphonePermission = completion;
            ActivityCompat.RequestPermissions(this, [Manifest.Permission.RecordAudio], MicrophonePermissionRequest);
        });

        return completion.Task;
    }

    private static async void ForwardPermissionAsync(Task<bool> pending, TaskCompletionSource<bool> completion) =>
        completion.TrySetResult(await pending);

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        if (requestCode != MicrophonePermissionRequest)
            return;

        _microphonePermission?.TrySetResult(grantResults.Length > 0 && grantResults[0] == Permission.Granted);
        _microphonePermission = null;
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
            RequestChat(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        RequestChat(intent);
    }

    protected override void OnStart()
    {
        base.OnStart();
        SetIsInFront(true);
    }

    protected override void OnStop()
    {
        SetIsInFront(false);
        base.OnStop();
    }

    protected override void OnDestroy()
    {
        // A recreated activity still gets the answer; one closed for good never will
        if (IsFinishing && !IsChangingConfigurations)
        {
            _microphonePermission?.TrySetResult(false);
            _microphonePermission = null;
        }

        if (ReferenceEquals(Current, this))
            Current = null;

        base.OnDestroy();
    }

    private static void RequestChat(Intent? intent)
    {
        if (ChatNotifications.ReadTarget(intent) is not { } chat)
            return;

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
