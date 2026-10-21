namespace Client.Tools.Environment;

public static class WindowsEnvironment
{
    // GLib reads the variables through GetEnvironmentVariableW, which sees the process environment
    public static void SetVariable(string name, string value)
    {
        System.Environment.SetEnvironmentVariable(name, value);
    }
}
