namespace Client.Core.Media;

/// <summary>Process-wide media mode, configured by the host before creating services.</summary>
public static class MediaRuntime
{
    private static bool _isEnabled = true;
    public static bool IsEnabled => ApplicationRuntime.SupportsMedia && _isEnabled;

    public static void Configure(IEnumerable<string> arguments) => _isEnabled = !arguments.Contains("--no-media");
}
