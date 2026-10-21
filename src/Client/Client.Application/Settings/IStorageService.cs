using Client.Core.Settings;

namespace Client.Application.Settings;

/// <summary>
/// What of the chats is kept on this device: the messages and the images of messages.
/// </summary>
public interface IStorageService
{
    /// <summary>
    /// Puts the saved settings into effect. Called once at start, before a chat is opened.
    /// </summary>
    Task ApplyAsync();

    Task<StorageSettings> GetSettingsAsync();

    /// <summary>
    /// Saves the settings and puts them into effect at once. Turning the keeping of messages off
    /// removes the kept ones.
    /// </summary>
    Task SaveSettingsAsync(StorageSettings settings);

    Task<StorageUsage> GetUsageAsync();

    /// <summary>Removes the kept messages of every chat; they come from the server again.</summary>
    Task ClearChatCacheAsync();

    /// <summary>Removes the kept images; they are downloaded again when shown.</summary>
    Task ClearMediaCacheAsync();
}
