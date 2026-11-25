namespace Client.Core;

/// <summary>Host mode and platform capabilities for this process.</summary>
public static class ApplicationRuntime
{
    public static bool IsBrowser { get; private set; }
    public static bool SupportsUpdates => !IsBrowser;
    public static bool SupportsMedia => !IsBrowser;
    public static bool SupportsTray => !IsBrowser;
    public static bool SupportsCustomTitleBar => !IsBrowser;
    public static bool SupportsGlobalHotKeys => !IsBrowser;

    /// <summary>The browser host supplies --browser internally; desktop cannot enable it.</summary>
    public static void Configure(IEnumerable<string> arguments)
    {
        var isBrowser = arguments.Contains("--browser");
        if (isBrowser && !OperatingSystem.IsBrowser())
            throw new PlatformNotSupportedException("Browser mode is only available in the browser host.");

        IsBrowser = isBrowser;
    }
}
