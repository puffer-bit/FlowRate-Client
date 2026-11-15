using Android.App;
using Android.Views;
using Android.Widget;
using Avalonia.Styling;
using Client.Avalonia.Localization;

namespace Client.Droid.Platform;

/// <summary>Lets Android own message text selection, handles, and the copy toolbar.</summary>
internal static class NativeMessageText
{
    public static void Show(string text)
    {
        if (MainActivity.Current is not { IsFinishing: false, IsDestroyed: false } activity || string.IsNullOrEmpty(text))
            return;

        var theme = global::Avalonia.Application.Current?.ActualThemeVariant == ThemeVariant.Light
            ? global::Android.Resource.Style.ThemeMaterialLightDialogAlert
            : global::Android.Resource.Style.ThemeMaterialDialogAlert;
        using var context = new ContextThemeWrapper(activity, theme);
        using var builder = new AlertDialog.Builder(context);
        var dialog = builder.SetTitle(Strings.Get("chat.menu.selectText"))!
            .SetMessage(text)!
            .SetPositiveButton(global::Android.Resource.String.Ok, (_, _) => { })!
            .Create();
        if (dialog == null)
            return;
        dialog.DismissEvent += (_, _) => dialog.Dispose();
        dialog.SetCanceledOnTouchOutside(true);
        dialog.Show();
        dialog.FindViewById<TextView>(global::Android.Resource.Id.Message)?.SetTextIsSelectable(true);
    }
}
