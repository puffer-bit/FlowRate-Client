namespace Client.Application.Ports;

/// <summary>
/// Message attachments and avatars kept as files between runs, so they are downloaded once. The oldest unused
/// files go when the cache outgrows its limit.
/// </summary>
public interface IMediaCache
{
    /// <summary>
    /// How many bytes the cached media may take. Lowering it removes the files used longest ago.
    /// </summary>
    long SizeLimit { get; set; }

    /// <summary>How many bytes the cached media take.</summary>
    Task<long> GetSizeAsync();

    /// <summary>Removes every cached media file.</summary>
    Task ClearAsync();

    /// <returns>The file of the image, or null when it is not kept.</returns>
    string? Find(string coordinatorInstanceId, int imageId, bool preview);

    /// <summary>
    /// Keeps what <paramref name="write"/> puts into the stream it is given. Nothing is kept when
    /// it returns false or throws.
    /// </summary>
    /// <returns>The file of the image, or null when it was not kept.</returns>
    Task<string?> StoreAsync(string coordinatorInstanceId, int imageId, bool preview, Func<Stream, Task<bool>> write);

    /// <returns>The file of the avatar, or null when it is not kept.</returns>
    string? FindAvatar(string coordinatorInstanceId, string avatarId);

    /// <inheritdoc cref="StoreAsync"/>
    Task<string?> StoreAvatarAsync(string coordinatorInstanceId, string avatarId, Func<Stream, Task<bool>> write);

    /// <returns>The attachment with its original name, or null when it is not kept.</returns>
    string? FindFile(string coordinatorInstanceId, int fileId, string fileName);

    /// <inheritdoc cref="StoreAsync"/>
    Task<string?> StoreFileAsync(string coordinatorInstanceId, int fileId, string fileName, Func<Stream, Task<bool>> write);
}
