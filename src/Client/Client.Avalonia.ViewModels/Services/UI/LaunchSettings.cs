using Client.Tools.AppInfo;

namespace Client.Services;

/// <summary>Availability of settings overridden by the desktop command line.</summary>
public static class LaunchSettings
{
    public static bool CanAutoConnect => !LaunchOptions.Current.PreventAutoConnect;
    public static bool CanUpdate => Client.Core.ApplicationRuntime.SupportsUpdates && !LaunchOptions.Current.DisableUpdates;
    public static bool CanChangeLanguage => LaunchOptions.Current.Language == null;
    public static bool CanChangeLogLevel => LaunchOptions.Current.LogLevel == null;
    public static bool CanWriteLogFiles => !LaunchOptions.Current.NoFileLogging;
    public static bool CanUseGlobalHotKeys => Client.Core.ApplicationRuntime.SupportsGlobalHotKeys && !LaunchOptions.Current.SafeMode;
    public static bool CanUseMedia => Client.Core.Media.MediaRuntime.IsEnabled && !LaunchOptions.Current.DisableMedia;
}
