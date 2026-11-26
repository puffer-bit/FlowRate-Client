using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Serilog;

namespace Client.Services;

/// <summary>
/// Makes the desktop open flowrate:// links with the client, the way flowrate-linux.sh does, so that starting the
/// client once is enough. The menu entry of the script (flowrate.desktop) gets the link argument and the scheme and
/// keeps pointing at the installed copy while that is there. Without an entry a hidden one is written, which the
/// script replaces when it installs the client. The handler of the scheme is then set to that entry.
/// </summary>
internal static class LinuxLinkScheme
{
    private const string DesktopFileName = "flowrate.desktop";
    private const string Scheme = "x-scheme-handler/flowrate";

    public static void Register()
    {
        if (!OperatingSystem.IsLinux())
            return;

        // An AppImage runs from a mount that is gone after it exits: the runtime names the file itself
        var executable = Environment.GetEnvironmentVariable("APPIMAGE");
        if (string.IsNullOrEmpty(executable) || !File.Exists(executable))
            executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
            return;

        try
        {
            var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrEmpty(dataHome))
                dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            var file = Path.Combine(dataHome, "applications", DesktopFileName);

            var current = File.Exists(file) ? File.ReadAllLines(file) : null;
            if (current is { Length: 0 })
                current = null;
            var updated = current == null ? HiddenEntry(executable) : WithScheme(current, executable);
            if (current == null || !current.SequenceEqual(updated))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                var temporary = file + ".new";
                File.WriteAllLines(temporary, updated);
                File.Move(temporary, file, overwrite: true);
                Log.Information("flowrate:// links now open {Executable} ({File})", TargetOf(updated) ?? executable, file);
            }

            if (RunXdgMime("query", "default", Scheme)?.Trim() != DesktopFileName)
                RunXdgMime("default", DesktopFileName, Scheme);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Links do not open the client, everything else works
            Log.Warning(exception, "Could not register flowrate:// links");
        }
    }

    /// <summary>
    /// The entry with the link argument and the scheme. The client it starts stays while it is there, so a copy run
    /// from elsewhere does not take the links of the installed one; a client that is gone is replaced by this one.
    /// </summary>
    private static List<string> WithScheme(string[] lines, string executable)
    {
        var target = TargetOf(lines);
        if (target == null || !File.Exists(target))
            target = executable;

        var result = new List<string>();
        var hasMimeType = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("Exec=", StringComparison.Ordinal))
                result.Add(ExecLine(target));
            else if (line.StartsWith("TryExec=", StringComparison.Ordinal))
                result.Add("TryExec=" + target);
            else if (line.StartsWith("MimeType=", StringComparison.Ordinal))
            {
                hasMimeType = true;
                var types = line["MimeType=".Length..].Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
                if (!types.Contains(Scheme))
                    types.Add(Scheme);
                result.Add("MimeType=" + string.Join(';', types) + ";");
            }
            else
                result.Add(line);
        }
        if (!hasMimeType)
        {
            // Right after the last key of the entry, before blank lines at the end or another group
            var index = result.FindIndex(1, line => line.Length == 0 || line.StartsWith('['));
            result.Insert(index < 0 ? result.Count : index, $"MimeType={Scheme};");
        }
        return result;
    }

    private static List<string> HiddenEntry(string executable) =>
    [
        "[Desktop Entry]",
        "Type=Application",
        "Name=FlowRate",
        "Comment=Opens flowrate:// links in FlowRate",
        ExecLine(executable),
        "TryExec=" + executable,
        "Icon=flowrate",
        "Terminal=false",
        "NoDisplay=true",
        $"MimeType={Scheme};",
        "StartupWMClass=FlowRate"
    ];

    // Quoted as the Desktop Entry spec wants: inside double quotes \ " ` $ are escaped
    private static string ExecLine(string executable)
    {
        var quoted = string.Concat(executable.Select(c => c is '\\' or '"' or '`' or '$' ? $"\\{c}" : c.ToString()));
        return $"Exec=\"{quoted}\" %u";
    }

    private static string? TargetOf(IEnumerable<string> lines) =>
        lines.FirstOrDefault(line => line.StartsWith("TryExec=", StringComparison.Ordinal))?["TryExec=".Length..];

    private static string? RunXdgMime(params string[] arguments)
    {
        var info = new ProcessStartInfo("xdg-mime") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(info);
            if (process == null)
                return null;
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(5000))
            {
                process.Kill();
                return null;
            }
            return output.Result;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No xdg-utils: the desktop takes the scheme from the entry when it reads it
            return null;
        }
    }
}
