using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Client.Services;

internal static partial class LaunchConsole
{
    // A Windows GUI executable does not inherit its parent's console automatically.
    public static void Attach()
    {
        if (!OperatingSystem.IsWindows() || !AttachConsole(uint.MaxValue))
            return;
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(uint processId);
}
