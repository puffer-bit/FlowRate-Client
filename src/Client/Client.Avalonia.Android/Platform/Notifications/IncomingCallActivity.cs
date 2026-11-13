using System;
using System.Globalization;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using Client.Application.Ports;
using Client.Avalonia.Localization;
using Microsoft.Extensions.DependencyInjection;
using Color = Android.Graphics.Color;

namespace Client.Droid.Platform.Notifications;

/// <summary>
/// The screen of an incoming call, shown by the notification over the lock screen. It is native and
/// light, so it opens at once, with no wait for the interface of FlowRate or its connection: only
/// accepting opens FlowRate, declining goes to the server over HTTP.
/// </summary>
[Activity(
    Theme = "@style/AppTheme",
    Exported = false,
    LaunchMode = LaunchMode.SingleTop,
    ExcludeFromRecents = true,
    // A task of its own: closing the screen does not bring up FlowRate behind it
    TaskAffinity = "",
    ScreenOrientation = ScreenOrientation.Portrait,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class IncomingCallActivity : Activity
{
    private static readonly Color Background = Color.ParseColor("#16181D");
    private static readonly Color Secondary = Color.ParseColor("#A9AFBC");
    private static readonly Color AcceptColor = Color.ParseColor("#2FB96B");
    private static readonly Color DeclineColor = Color.ParseColor("#E5484D");

    private readonly Handler _handler = new(Looper.MainLooper!);
    private IncomingCall? _call;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        if (OperatingSystem.IsAndroidVersionAtLeast(27))
        {
            SetShowWhenLocked(true);
            SetTurnScreenOn(true);
        }
        else
        {
            Window?.AddFlags(WindowManagerFlags.ShowWhenLocked | WindowManagerFlags.TurnScreenOn);
        }
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        Window?.SetStatusBarColor(Background);
        Window?.SetNavigationBarColor(Background);

        // The interface may not have run in this process
        Strings.SetLanguage(CultureInfo.CurrentUICulture.Name);
        IncomingCallNotifications.Closed += OnClosed;
        Show(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Show(intent);
    }

    protected override void OnDestroy()
    {
        IncomingCallNotifications.Closed -= OnClosed;
        _handler.RemoveCallbacksAndMessages(null);
        base.OnDestroy();
    }

    private void Show(Intent? intent)
    {
        _handler.RemoveCallbacksAndMessages(null);
        if (IncomingCall.ReadFrom(intent) is not { } call || !IncomingCallNotifications.IsPending(this, call))
        {
            Finish();
            return;
        }

        _call = call;
        SetContentView(CreateView(call));

        var left = call.ExpiresAt - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (left <= 0)
            Finish();
        else
            _handler.PostDelayed(Finish, left);
    }

    private void OnClosed(string coordinatorId, Guid? callId)
    {
        if (_call is { } call && call.CoordinatorId == coordinatorId && (callId == null || callId == call.CallId))
            RunOnUiThread(Finish);
    }

    private void OnAccept()
    {
        if (_call is not { } call)
            return;

        StartActivity(IncomingCallNotifications.CreateAcceptIntent(this, call));
        Finish();
    }

    private void OnDecline()
    {
        if (_call is not { } call)
            return;

        // The process lives on for the request: the screen is closed, the notification goes with the call
        _ = Task.Run(() => IncomingCallNotifications.DeclineAsync(ApplicationContext!, call));
        Finish();
    }

    private View CreateView(IncomingCall call)
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(Background);
        root.SetGravity(GravityFlags.CenterHorizontal);
        root.SetFitsSystemWindows(true);
        root.SetPadding(Dp(24), Dp(24), Dp(24), Dp(48));

        // The caller, in the upper middle of the screen
        var caller = new LinearLayout(this) { Orientation = Orientation.Vertical };
        caller.SetGravity(GravityFlags.Center);
        caller.AddView(CreateAvatar(call), new LinearLayout.LayoutParams(Dp(128), Dp(128)));

        var name = new TextView(this)
        {
            Text = call.CallerName,
            TextSize = 28,
            Gravity = GravityFlags.Center
        };
        name.SetTextColor(Color.White);
        name.SetMaxLines(2);
        name.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
        var nameLayout = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = Dp(24)
        };
        caller.AddView(name, nameLayout);

        var subtitle = new TextView(this)
        {
            Text = Strings.Get("calls.incoming"),
            TextSize = 16,
            Gravity = GravityFlags.Center
        };
        subtitle.SetTextColor(Secondary);
        var subtitleLayout = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = Dp(8)
        };
        caller.AddView(subtitle, subtitleLayout);
        root.AddView(caller, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));

        // Declining on the left and accepting on the right, as on the call screen of a phone
        var buttons = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        buttons.AddView(CreateButton(Strings.Get("calls.decline"), Resource.Drawable.ic_call_decline, DeclineColor, OnDecline),
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        buttons.AddView(CreateButton(Strings.Get("calls.accept"), Resource.Drawable.ic_call_accept, AcceptColor, OnAccept),
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        root.AddView(buttons, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        return root;
    }

    private View CreateAvatar(IncomingCall call)
    {
        var picture = ChatNotifications.FindRoundAvatar(call.CoordinatorId, call.CallerAvatarUrl,
            PushMessageHandler.Services.Value.GetService<IMediaCache>());
        if (picture != null)
        {
            var image = new ImageView(this);
            image.SetImageBitmap(picture);
            image.SetScaleType(ImageView.ScaleType.CenterCrop);
            return image;
        }

        // No avatar kept: the first letter of the name in a circle
        var letter = new TextView(this)
        {
            Text = string.IsNullOrEmpty(call.CallerName) ? "?" : char.ToUpperInvariant(call.CallerName[0]).ToString(),
            TextSize = 52,
            Gravity = GravityFlags.Center
        };
        letter.SetTextColor(Color.White);
        letter.Background = Circle(Color.ParseColor("#3A3F4B"));
        return letter;
    }

    private View CreateButton(string label, int icon, Color color, Action click)
    {
        var column = new LinearLayout(this) { Orientation = Orientation.Vertical };
        column.SetGravity(GravityFlags.CenterHorizontal);

        var button = new ImageButton(this) { ContentDescription = label };
        button.SetImageResource(icon);
        button.SetScaleType(ImageView.ScaleType.FitCenter);
        button.SetPadding(Dp(20), Dp(20), Dp(20), Dp(20));
        button.Background = new RippleDrawable(
            global::Android.Content.Res.ColorStateList.ValueOf(Color.Argb(64, 255, 255, 255)), Circle(color), null);
        button.Click += (_, _) => click();
        column.AddView(button, new LinearLayout.LayoutParams(Dp(76), Dp(76)));

        var text = new TextView(this) { Text = label, TextSize = 14, Gravity = GravityFlags.Center };
        text.SetTextColor(Color.White);
        var textLayout = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = Dp(12)
        };
        column.AddView(text, textLayout);

        return column;
    }

    private static ShapeDrawable Circle(Color color)
    {
        var circle = new ShapeDrawable(new global::Android.Graphics.Drawables.Shapes.OvalShape());
        circle.Paint.Color = color;
        return circle;
    }

    private int Dp(int value) =>
        (int)TypedValue.ApplyDimension(ComplexUnitType.Dip, value, Resources!.DisplayMetrics);
}
