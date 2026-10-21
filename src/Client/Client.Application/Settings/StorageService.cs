using Client.Application.Chat;
using Client.Application.Ports;
using Client.Application.Ports.Repositories;
using Client.Core.Settings;

namespace Client.Application.Settings;

public class StorageService : IStorageService
{
    private const long Megabyte = 1024 * 1024;

    private readonly IStorageSettingsRepository _settings;
    private readonly IChatCacheRepository _chatCache;
    private readonly IChatCacheStore _chatCaches;
    private readonly IMediaCache _mediaCache;

    public StorageService(IStorageSettingsRepository settings, IChatCacheRepository chatCache,
        IChatCacheStore chatCaches, IMediaCache mediaCache)
    {
        _settings = settings;
        _chatCache = chatCache;
        _chatCaches = chatCaches;
        _mediaCache = mediaCache;
    }

    public async Task ApplyAsync() => Apply(await _settings.GetAsync());

    public Task<StorageSettings> GetSettingsAsync() => _settings.GetAsync();

    public async Task SaveSettingsAsync(StorageSettings settings)
    {
        await _settings.SaveAsync(settings);
        Apply(settings);

        // Messages the user does not want kept do not stay on the device
        if (!settings.IsChatCacheEnabled)
            await _chatCache.RemoveAllAsync();
    }

    public async Task<StorageUsage> GetUsageAsync()
    {
        var (messages, bytes) = await _chatCache.GetUsageAsync();
        return new StorageUsage(messages, bytes, await _mediaCache.GetSizeAsync());
    }

    public Task ClearChatCacheAsync() => _chatCache.RemoveAllAsync();

    public Task ClearMediaCacheAsync() => _mediaCache.ClearAsync();

    private void Apply(StorageSettings settings)
    {
        _chatCaches.IsEnabled = settings.IsChatCacheEnabled;
        _chatCache.MaxMessages = settings.ChatCacheMessagesPerChat;
        _mediaCache.SizeLimit = settings.MediaCacheLimitMegabytes * Megabyte;
    }
}
