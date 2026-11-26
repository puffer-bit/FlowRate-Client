using System;
using Microsoft.Win32;

namespace Client.Services;

/// <summary>
/// Makes Windows open flowrate:// links with the installed client: the link comes as the argument of a new process,
/// which hands it to the running one. Written by the installer and every update, so it follows the installed copy,
/// and removed by the uninstaller.
/// </summary>
internal static class WindowsLinkScheme
{
    private const string Key = @"Software\Classes\flowrate";

    public static void Register()
    {
        if (!OperatingSystem.IsWindows() || Environment.ProcessPath is not { } executable)
            return;

        try
        {
            using var scheme = Registry.CurrentUser.CreateSubKey(Key);
            scheme.SetValue("", "URL:FlowRate link");
            scheme.SetValue("URL Protocol", "");
            using (var icon = scheme.CreateSubKey("DefaultIcon"))
                icon.SetValue("", $"\"{executable}\",0");
            using var command = scheme.CreateSubKey(@"shell\open\command");
            command.SetValue("", $"\"{executable}\" \"%1\"");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException
                                              or System.IO.IOException)
        {
            // Links do not open the client, everything else works
        }
    }

    public static void Unregister()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(Key, throwOnMissingSubKey: false);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException
                                              or System.IO.IOException)
        {
        }
    }
}
