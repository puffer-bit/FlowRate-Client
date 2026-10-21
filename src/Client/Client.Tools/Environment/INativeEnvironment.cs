using System.Runtime.InteropServices;

namespace Client.Tools.Environment;

public interface INativeEnvironment
{
    static void Set(string name, string value)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            WindowsEnvironment.SetVariable(name, value);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            LinuxEnvironment.SetVariable(name, value);
        }
        else
        {
            throw new PlatformNotSupportedException("Failed to find native environment implementation");
        }
    }
}