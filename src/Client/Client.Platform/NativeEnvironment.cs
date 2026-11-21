namespace Client.Platform;

/// <summary>
/// The environment variables native libraries read. On Linux and macOS
/// <see cref="System.Environment.SetEnvironmentVariable(string, string)"/> changes only the copy of
/// the runtime, and a library reading getenv does not see it, so the variable goes through setenv too.
/// </summary>
public static class NativeEnvironment
{
    /// <summary>
    /// Sets the variable for managed code and for the native environment. Must run before the
    /// library that reads it starts: most read their variables once.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">On a system other than Windows, Linux or macOS.</exception>
    public static void SetVariable(string name, string value)
    {
        if (OperatingSystem.IsWindows())
            WindowsEnvironment.SetVariable(name, value);
        else if (OperatingSystem.IsLinux())
            LinuxEnvironment.SetVariable(name, value);
        else if (OperatingSystem.IsMacOS())
            MacEnvironment.SetVariable(name, value);
        else
            throw new PlatformNotSupportedException("No native environment for this system");
    }
}
