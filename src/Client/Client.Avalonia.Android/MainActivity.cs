using System;
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
