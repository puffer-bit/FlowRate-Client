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
}
