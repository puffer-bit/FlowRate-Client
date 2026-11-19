namespace Client.Tools.AppInfo;

public static class AppDirectories
{
    /// <summary>
    /// The folder of everything the client keeps between runs: the database, the logs, the caches and
    /// the sound packs. It is in the profile of the user, "%LocalAppData%\FlowRate" on Windows and
    /// "~/.local/share/FlowRate" on Linux, not next to the executable: an update replaces the folder
    /// of the application as a whole, and on Linux that folder is a read-only AppImage.
    /// </summary>
    public static string Data { get; private set; } = DefaultData;

    private static string DefaultData => Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData,
            System.Environment.SpecialFolderOption.DoNotVerify),
        "FlowRate");

    /// <summary>Resolves the desktop data directory without accessing the filesystem.</summary>
    public static string ResolveDataDirectory(IReadOnlyList<string> args)
    {
        string? directory = null;
        for (var i = 0; i < args.Count; i++)
        {
            var argument = args[i];
            if (argument != "--data-dir" && !argument.StartsWith("--data-dir=", StringComparison.Ordinal))
                continue;

            if (directory != null)
                throw new ArgumentException("Specify --data-dir only once.");

            if (argument == "--data-dir")
            {
                if (++i >= args.Count || args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException("--data-dir requires a directory path.");
                directory = args[i];
            }
            else
                directory = argument["--data-dir=".Length..];

            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("--data-dir requires a non-empty directory path.");
        }

        return Path.GetFullPath(directory ?? DefaultData);
    }

    /// <summary>Call once at desktop startup, before initializing logging or any data consumers.</summary>
    public static void ConfigureFromArguments(IReadOnlyList<string> args)
    {
        var directory = ResolveDataDirectory(args);
        Directory.CreateDirectory(directory);
        // Verify write access before any service opens its files. Delete the probe on close.
        using (new FileStream(Path.Combine(directory, $".flowrate-write-test-{Guid.NewGuid():N}"),
                   FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
        {
        }
        Data = directory;
    }
}
