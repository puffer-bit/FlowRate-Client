using System.Reflection;

namespace Client.Tools.AppInfo;

public static class AppInformation
{
    /// <summary>
    /// Where the code of the client is, and its releases the updater takes.
    /// </summary>
    public const string RepositoryUrl = "https://github.com/puffer-bit/FlowRate-Client";

    public static string GetClientVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";
    }

    /// <summary>
    /// The version with the number of the build and its commit, "0.5.1-build2610130042(c6f00e6)".
    /// </summary>
    public static string GetClientBuildVersion() => WithBuildNumber(GetClientVersion(), Assembly.GetEntryAssembly());

    /// <summary>
    /// Adds the number of the build and, in brackets, the commit it was made from to a version:
    /// "0.5.1-build2610130042(c6f00e6)". The number is the moment of the build in UTC, which a Debug
    /// build does not have: "0.5.1(c6f00e6)". Both are put into the assembly of the application by
    /// BuildNumber.targets, so each platform has its own.
    /// </summary>
    /// <param name="assembly">The assembly of the application: Android has no entry assembly to find it by.</param>
    public static string WithBuildNumber(string version, Assembly? assembly)
    {
        if (GetMetadataValue(assembly, "BuildNumber") is { } number)
            version = $"{version}-build{number}";

        // Missing when the build was made outside a git repository
        return GetMetadataValue(assembly, "BuildCommit") is { } commit ? $"{version}({commit})" : version;
    }

    public static string GetMetadataValue(string key) => GetMetadataValue(Assembly.GetEntryAssembly(), key) ?? "unknown";

    private static string? GetMetadataValue(Assembly? assembly, string key)
    {
        return assembly?
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == key)?.Value;
    }

    public static string GetSupportedApiVersion() => GetMetadataValue("SupportedApiVersion");
}
