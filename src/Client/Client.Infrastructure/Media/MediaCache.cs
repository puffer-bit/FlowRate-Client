using System.Globalization;
using Client.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Client.Infrastructure.Media;

/// <summary>
/// Keeps the images in a directory, a subdirectory per coordinator. A file is written under a
/// temporary name and renamed when complete, so a half-written image is never found.
/// </summary>
public sealed partial class MediaCache : IMediaCache
{
    public const string FolderName = "MediaCache";
    public const long DefaultSizeLimit = 512L * 1024 * 1024;

    private const string TemporaryExtension = ".part";

    // Trimming walks the whole directory, so it runs once per this many bytes written
    private const long TrimInterval = 32L * 1024 * 1024;

    private readonly string _directory;
    private long _sizeLimit;
    private readonly ILogger<MediaCache> _logger;
    private readonly Lock _trimLock = new();
    private long _writtenSinceTrim;

    public MediaCache(string directory, ILogger<MediaCache> logger, long sizeLimit = DefaultSizeLimit)
    {
        _directory = directory;
        _sizeLimit = sizeLimit;
        _logger = logger;

        // What an earlier run left: unfinished files, and more than the limit if it was lowered
        _writtenSinceTrim = TrimInterval;
    }

    public long SizeLimit
    {
        get => Interlocked.Read(ref _sizeLimit);
        set
        {
            // Less room than before: what does not fit anymore goes now, not after the next download
            if (Interlocked.Exchange(ref _sizeLimit, value) > value)
                _ = Task.Run(Trim);
        }
    }

    public Task<long> GetSizeAsync() => Task.Run(() =>
    {
        try
        {
            return Directory.Exists(_directory)
                ? new DirectoryInfo(_directory).EnumerateFiles("*", SearchOption.AllDirectories)
                    .Where(file => file.Extension != TemporaryExtension)
                    .Sum(file => file.Length)
                : 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0L;
        }
    });

    public Task ClearAsync() => Task.Run(() =>
    {
        if (!Directory.Exists(_directory))
            return;

        try
        {
            // A download in progress keeps its unfinished file and is kept when it ends
            foreach (var file in new DirectoryInfo(_directory).EnumerateFiles("*", SearchOption.AllDirectories)
                         .Where(file => file.Extension != TemporaryExtension))
                TryDelete(file.FullName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogTrimFailed(exception);
        }
    });

    public string? Find(string coordinatorInstanceId, int imageId, bool preview) =>
        Find(PathOf(coordinatorInstanceId, imageId, preview));

    public string? FindAvatar(string coordinatorInstanceId, string avatarId) =>
        Find(PathOfAvatar(coordinatorInstanceId, avatarId));

    public Task<string?> StoreAsync(string coordinatorInstanceId, int imageId, bool preview,
        Func<Stream, Task<bool>> write) =>
        StoreAsync(PathOf(coordinatorInstanceId, imageId, preview), write);

    public Task<string?> StoreAvatarAsync(string coordinatorInstanceId, string avatarId,
        Func<Stream, Task<bool>> write) =>
        StoreAsync(PathOfAvatar(coordinatorInstanceId, avatarId), write);

    private static string? Find(string? path)
    {
        if (path == null || !File.Exists(path))
            return null;

        try
        {
            // The time of the last use is what trimming goes by
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return path;
    }

    private async Task<string?> StoreAsync(string? path, Func<Stream, Task<bool>> write)
    {
        if (path == null)
            return null;

        var temporary = $"{path}.{Guid.NewGuid():N}{TemporaryExtension}";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            long written;
            bool complete;
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             81920, FileOptions.Asynchronous))
            {
                complete = await write(stream);
                written = stream.Length;
            }

            if (!complete)
            {
                File.Delete(temporary);
                return null;
            }

            File.Move(temporary, path, overwrite: true);

            if (Interlocked.Add(ref _writtenSinceTrim, written) >= TrimInterval)
                _ = Task.Run(Trim);

            return path;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogStoreFailed(exception, path);
            TryDelete(temporary);
            return null;
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    /// <summary>
    /// Deletes the files used longest ago until the cache fits its limit, and the leftovers of
    /// writes that never finished.
    /// </summary>
    internal void Trim()
    {
        if (!_trimLock.TryEnter())
            return;

        try
        {
            Interlocked.Exchange(ref _writtenSinceTrim, 0);
            if (!Directory.Exists(_directory))
                return;

            var files = new DirectoryInfo(_directory).EnumerateFiles("*", SearchOption.AllDirectories).ToList();

            // A write in progress is younger than this
            var abandonedBefore = DateTime.UtcNow.AddHours(-1);
            foreach (var file in files.Where(f => f.Extension == TemporaryExtension && f.LastWriteTimeUtc < abandonedBefore))
                TryDelete(file.FullName);

            var kept = files.Where(f => f.Extension != TemporaryExtension).ToList();
            var total = kept.Sum(f => f.Length);
            foreach (var file in kept.OrderBy(f => f.LastAccessTimeUtc))
            {
                if (total <= SizeLimit)
                    break;

                total -= file.Length;
                TryDelete(file.FullName);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogTrimFailed(exception);
        }
        finally
        {
            _trimLock.Exit();
        }
    }

    private string? PathOf(string coordinatorInstanceId, int imageId, bool preview) =>
        PathOf(coordinatorInstanceId,
            imageId.ToString(CultureInfo.InvariantCulture) + (preview ? ".preview" : ".original"));

    private string? PathOfAvatar(string coordinatorInstanceId, string avatarId) =>
        avatarId.Length > 0 && avatarId.All(char.IsAsciiLetterOrDigit)
            ? PathOf(coordinatorInstanceId, avatarId + ".avatar")
            : null;

    private string? PathOf(string coordinatorInstanceId, string fileName)
    {
        // The id comes from the server and becomes a directory name
        if (coordinatorInstanceId.Length == 0 || !coordinatorInstanceId.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            return null;

        return Path.Combine(_directory, coordinatorInstanceId, fileName);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    [LoggerMessage(LogLevel.Warning, "The image was not kept as {Path}")]
    private partial void LogStoreFailed(Exception exception, string path);

    [LoggerMessage(LogLevel.Warning, "Removing images from the media cache failed")]
    private partial void LogTrimFailed(Exception exception);
}
