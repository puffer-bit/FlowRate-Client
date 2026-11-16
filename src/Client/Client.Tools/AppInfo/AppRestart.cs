using System.Diagnostics;

namespace Client.Tools.AppInfo;

public static class AppRestart
{
    private const string ParentVariable = "FLOWRATE_RESTART_PARENT_PID";

    public static void WaitForPreviousProcess()
    {
        var value = System.Environment.GetEnvironmentVariable(ParentVariable);
        System.Environment.SetEnvironmentVariable(ParentVariable, null);
        if (!int.TryParse(value, out var parentId) || parentId == System.Environment.ProcessId)
            return;
        try
        {
            using var parent = Process.GetProcessById(parentId);
            if (!parent.WaitForExit(10_000))
                throw new TimeoutException("The previous application process did not exit.");
        }
        catch (ArgumentException)
        {
            // The previous process has already exited.
        }
    }

    public static void WithoutMedia()
    {
        StartNewProcess("--no-media");

        // Failed native initialization can leave DLL shutdown handlers stuck. Recovery must not
        // run those handlers: the OS releases the process's database handles and instance mutex.
        ExitAfterNativeFailure();
    }

    /// <summary>
    /// Starts the application again with the arguments of this process, plus <paramref name="extraArgument"/>
    /// when it is not among them. The new process waits for this one to exit, see <see cref="WaitForPreviousProcess"/>.
    /// </summary>
    public static void StartNewProcess(string? extraArgument = null)
    {
        var executable = System.Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine the application executable.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        var arguments = System.Environment.GetCommandLineArgs();
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(arguments[0]);
        foreach (var argument in arguments[1..])
            start.ArgumentList.Add(argument);
        if (extraArgument != null && !arguments.Skip(1).Contains(extraArgument))
            start.ArgumentList.Add(extraArgument);
        start.Environment[ParentVariable] = System.Environment.ProcessId.ToString();
        using var child = Process.Start(start)
            ?? throw new InvalidOperationException("Cannot start the application.");
    }

    public static void ExitAfterNativeFailure()
    {
        using var current = Process.GetCurrentProcess();
        current.Kill();
    }
}
