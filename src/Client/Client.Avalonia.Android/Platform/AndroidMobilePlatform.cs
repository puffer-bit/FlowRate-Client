using Android.Content;
using Android.Content.PM;
using Client.Droid.Platform.Media;
using Client.Mobile.Platform;

namespace Client.Droid.Platform;

/// <summary>What the shared mobile views get of Android.</summary>
internal sealed class AndroidMobilePlatform : IMobilePlatform
{
    public string AppVersion => ApplicationDetails.Version;

    public string SystemDescription => ApplicationDetails.SystemDescription;

    public ICallAudioRoutes CallAudio => AndroidCallAudioRouter.Instance;

    public void Exit(bool removeFromRecents)
    {
        if (removeFromRecents)
            MainActivity.Current?.FinishAndRemoveTask();
        else
            MainActivity.Current?.Finish();
    }

    public bool DismissTextMenu() => NativeTextMenu.TryDismissActive();

    public void ShowSelectableText(string text) => NativeMessageText.Show(text);

    public void HapticTick() => Haptics.Tick();

    public void SetLandscape(bool landscape)
    {
        if (MainActivity.Current is { } activity)
            activity.RequestedOrientation = landscape ? ScreenOrientation.SensorLandscape : ScreenOrientation.Unspecified;
    }

    public void ShareText(string text, string title)
    {
        if (MainActivity.Current is not { } activity)
            return;

        var send = new Intent(Intent.ActionSend);
        send.SetType("text/plain");
        send.PutExtra(Intent.ExtraText, text);
        activity.StartActivity(Intent.CreateChooser(send, title));
    }

    public System.Threading.Tasks.Task<string?> ScanQrCodeAsync() => QrScanner.ScanAsync();
}
