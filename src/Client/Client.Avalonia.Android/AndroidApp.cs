using System;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Client.Avalonia.Icons;
using ReactiveUI.Avalonia;

namespace Client.Droid;

[Application]
public class AndroidApp : AvaloniaAndroidApplication<App>
{
    protected AndroidApp(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder).WithInterFont().WithEmojiFont().LogToTrace().UseReactiveUI(_ => { });
    }
}
