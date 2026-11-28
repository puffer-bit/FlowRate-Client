using System;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Client.Avalonia.Icons;
using Client.Droid.Platform;
using Client.Services.Logging;
using Client.Tools.AppInfo;
using ReactiveUI.Avalonia;

namespace Client.Droid;

[Application]
public class AndroidApp : AvaloniaAndroidApplication<App>
{
    protected AndroidApp(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }

    public override void OnCreate()
    {
        // First of all, so that the start of the interface or of a push is in the journal too
        AndroidLogging.Start();
        AppInformation.ClientVersion = ApplicationDetails.PackageVersion;

        base.OnCreate();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder).WithAppFonts().LogToJournal().UseReactiveUI(_ => { });
    }
}
