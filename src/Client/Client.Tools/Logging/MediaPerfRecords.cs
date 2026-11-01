using System.Globalization;
using System.Text;

namespace Client.Tools.Logging;

/// <summary>
/// The detailed statistics of the media, one JSON object a line, in <c>media-yyyyMMdd.jsonl</c> next to
/// the files of the journal. Kept out of the journal itself: a line every second of every stream would
/// bury it, and the console with it. Follows <see cref="ClientLogging.IsWritingToFiles"/>.
/// </summary>
public static class MediaPerfRecords
{
    public const string FilePrefix = "media-";
    public const string FileExtension = ".jsonl";

    // A day of a few streams at a reading a second is a few tens of MB; past this the rest of the day is left out
    private const long MaxFileBytes = 64L * 1024 * 1024;

    // The files of the days before, as many as the other kinds keep
    private const int RetainedFiles = 7;

    private static readonly Lock Lock = new();
    private static StreamWriter? _writer;
    private static string? _path;

    /// <summary>Appends one line, a whole JSON object. Any thread.</summary>
    public static void Write(string json)
    {
        if (ClientLogging.LogDirectory is not { } directory || !ClientLogging.IsWritingToFiles)
            return;

        try
        {
            lock (Lock)
            {
                var path = Path.Combine(directory,
                    FilePrefix + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + FileExtension);

                if (path != _path)
                    Open(directory, path);

                if (_writer == null || _writer.BaseStream.Length > MaxFileBytes)
                    return;

                _writer.WriteLine(json);
                _writer.Flush();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The statistics are not worth an error of their own; the next line tries again
            Close();
        }
    }

    // Under the lock
    private static void Open(string directory, string path)
    {
        Close();
        Directory.CreateDirectory(directory);

        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
            new UTF8Encoding(false));
        _path = path;

        foreach (var old in new DirectoryInfo(directory).EnumerateFiles(FilePrefix + "*" + FileExtension)
                     .OrderByDescending(file => file.Name, StringComparer.Ordinal)
                     .Skip(RetainedFiles + 1))
        {
            try
            {
                old.Delete();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Held by something else; deleted with a later day
            }
        }
    }

    /// <summary>Closes the file being written, so it can be deleted or moved.</summary>
    public static void Close()
    {
        lock (Lock)
        {
            _writer?.Dispose();
            _writer = null;
            _path = null;
        }
    }
}
