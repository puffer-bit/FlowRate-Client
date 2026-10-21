namespace Client.Core.Settings;

/// <summary>
/// User preferences of the Data and storage tab: what of the chats is kept on this device.
/// </summary>
public class StorageSettings
{
    public static IReadOnlyList<int> ChatCacheMessageChoices { get; } = [500, 1000, 2000, 5000];
    public static IReadOnlyList<int> MediaCacheLimitChoices { get; } = [128, 256, 512, 1024, 2048, 4096];

    /// <summary>
    /// Messages are kept between runs, so a chat opens at once. Turned off, nothing of the chats
    /// is written to the device and what was kept is removed.
    /// </summary>
    public bool IsChatCacheEnabled { get; set; } = true;

    /// <summary>How many of the newest messages of each chat are kept.</summary>
    public int ChatCacheMessagesPerChat { get; set; } = 2000;

    /// <summary>How much space the images of messages may take, in megabytes.</summary>
    public int MediaCacheLimitMegabytes { get; set; } = 512;
}

/// <summary>What the kept chats take on this device.</summary>
/// <param name="ChatBytes">The size of the kept messages themselves, without what the database adds.</param>
public readonly record struct StorageUsage(int ChatMessages, long ChatBytes, long MediaBytes);
