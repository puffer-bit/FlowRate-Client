using System.IO.Compression;
using System.Text;

namespace Client.Tools.Logging;

/// <summary>
/// The files of the journal in <see cref="ClientLogging.LogDirectory"/>: what they take, clearing them and
/// putting them into one archive for a bug report.
/// </summary>
public static class LogFiles
{
    // The kinds of files ClientLogging writes, each continued in a new file by day and by size
    private static readonly string[] Kinds = ["app-", "gstreamer-", "libwebrtc-"];

    /// <summary>
    /// The files of the journal, the newest first.
    /// </summary>
    public static IReadOnlyList<FileInfo> List(string directory)
    {
        if (!Directory.Exists(directory))
            return [];

        return new DirectoryInfo(directory).EnumerateFiles("*.txt")
            .Where(file => Kinds.Any(kind => file.Name.StartsWith(kind, StringComparison.Ordinal)))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();
    }

    public static long Size(string directory) => List(directory).Sum(file => file.Length);

    /// <summary>
    /// Deletes the files of the journal but the newest of each kind, which is still being written: deleted,
    /// it would take what comes next with it.
    /// </summary>
    /// <returns>How many files were deleted.</returns>
    public static int Clear(string directory)
    {
        var deleted = 0;

        foreach (var group in List(directory).GroupBy(KindOf))
        {
            foreach (var file in group.Skip(1))
            {
                try
                {
                    file.Delete();
                    deleted++;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Held by something else; it goes with the next clearing or with the retention
                }
            }
        }

        return deleted;
    }

    /// <summary>
    /// Writes a zip of the files of the journal, a description of the system and the last entries kept in
    /// memory, which are there even when the journal is not written to files.
    /// </summary>
    /// <param name="directory">Where the files are, or null when there are none.</param>
    /// <param name="systemInfo">Written as system.txt.</param>
    public static async Task WriteArchiveAsync(Stream destination, string? directory, string systemInfo,
        IReadOnlyList<LogEntry> recent, CancellationToken cancellationToken = default)
    {
        await using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

        await WriteTextAsync(archive, "system.txt", systemInfo, cancellationToken);
        await WriteTextAsync(archive, "recent.txt", FormatRecent(recent), cancellationToken);

        if (directory == null)
            return;

        foreach (var file in List(directory))
        {
            try
            {
                // The files being written are open in the journal, which lets them be read
                await using var source = new FileStream(file.FullName, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                var entry = archive.CreateEntry($"logs/{file.Name}", CompressionLevel.Optimal);
                await using var target = await entry.OpenAsync(cancellationToken);
                await source.CopyToAsync(target, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Deleted in the meantime, or held: the archive goes without it
            }
        }
    }

    private static string KindOf(FileInfo file) =>
        Kinds.First(kind => file.Name.StartsWith(kind, StringComparison.Ordinal));

    private static string FormatRecent(IReadOnlyList<LogEntry> entries)
    {
        var text = new StringBuilder();

        foreach (var entry in entries)
        {
            text.Append('[').Append(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
                .Append(' ').Append(entry.Level).Append("] [").Append(entry.Category).Append("] ")
                .AppendLine(entry.Message);

            if (entry.Exception != null)
                text.AppendLine(entry.Exception);
        }

        return text.ToString();
    }

    private static async Task WriteTextAsync(ZipArchive archive, string name, string text,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = await entry.OpenAsync(cancellationToken);
        await stream.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
    }
}
