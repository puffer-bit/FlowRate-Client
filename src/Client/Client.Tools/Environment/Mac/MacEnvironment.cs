using System.Runtime.InteropServices;

namespace Client.Tools.Environment;

public static class MacEnvironment
{
    // Ansi marshals as UTF-8 on Unix
    [DllImport("/usr/lib/libSystem.B.dylib", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int setenv(string name, string value, int overwrite);

    /// <summary>
    /// Sets the variable for managed code and for the native environment, which is what native
    /// libraries read through getenv.
    /// </summary>
    public static void SetVariable(string name, string value)
    {
        System.Environment.SetEnvironmentVariable(name, value);
        setenv(name, value, 1);
    }
}
