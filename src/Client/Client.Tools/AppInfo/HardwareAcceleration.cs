namespace Client.Tools.AppInfo;

/// <summary>
/// Whether the GPU draws the interface and encodes and decodes video. Off, everything is done by the
/// CPU: the renderer of the window, the video encoders and the decoders.
/// </summary>
/// <remarks>
/// Settled once at start, since the renderer is chosen before the window opens; a change applies
/// after a restart. The user's choice is kept in a file of the data folder rather than in the
/// database, which may stay encrypted until the user unlocks it, long after the renderer is chosen.
/// </remarks>
public static class HardwareAcceleration
{
    // Present while the user has turned acceleration off
    private const string FileName = "hardware-acceleration-off";

    private static string FilePath => Path.Combine(AppDirectories.Data, FileName);

    /// <summary>Whether this process uses the GPU, as settled at start.</summary>
    public static bool IsEnabled => !IsForcedOff && !IsTurnedOffAtStart;

    /// <summary>Turned off for this launch by --software-rendering, whatever the user saved.</summary>
    public static bool IsForcedOff => LaunchOptions.Current.SoftwareRendering;

    /// <summary>The saved choice of the user was off when the process started.</summary>
    public static bool IsTurnedOffAtStart { get; private set; }

    /// <summary>Reads the saved choice. Call once at start, after the data folder is configured.</summary>
    public static void Load() => IsTurnedOffAtStart = IsTurnedOffSaved();

    /// <summary>The saved choice of the user, which the next start uses.</summary>
    public static bool IsTurnedOffSaved()
    {
        try
        {
            return File.Exists(FilePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Saves the choice for the next start.</summary>
    public static void Save(bool isEnabled)
    {
        if (isEnabled)
        {
            File.Delete(FilePath);
            return;
        }

        Directory.CreateDirectory(AppDirectories.Data);
        File.WriteAllText(FilePath, "");
    }
}
