using System.Runtime.InteropServices;

namespace Client.Tools.Environment;

public static class LinuxEnvironment
{
    // Ansi marshals as UTF-8 on Unix, Unicode would pass UTF-16 and setenv would see a one-letter name.
    // The versioned name is always present, the bare "libc.so" is a linker script of the dev package.
    [DllImport("libc.so.6", SetLastError = true, CharSet = CharSet.Ansi)]
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
