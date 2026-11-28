namespace Client.Core.Media;

/// <summary>Process-wide media mode, configured by the host before creating services.</summary>
public static class MediaRuntime
{
    public static bool IsEnabled { get; private set; } = true;

    public static void Configure(IEnumerable<string> arguments) => IsEnabled = !arguments.Contains("--no-media");
}
