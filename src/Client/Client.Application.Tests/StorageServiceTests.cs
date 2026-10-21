using Client.Application.Chat;
using Client.Application.Ports;
using Client.Application.Ports.Repositories;
using Client.Application.Settings;
using Client.Core.Chat;
using Client.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Client.Application.Tests;

public class StorageServiceTests
{
    private static readonly ChatCacheKey Chat = new("coordinator", 1);

    private readonly MemorySettings _settings = new();
    private readonly MemoryChatCache _chatCache = new();
    private readonly MemoryMediaCache _mediaCache = new();
    private readonly ChatCacheStore _caches;
    private readonly StorageService _service;

    public StorageServiceTests()
    {
        _caches = new ChatCacheStore(_chatCache, NullLogger<ChatCacheStore>.Instance);
        _service = new StorageService(_settings, _chatCache, _caches, _mediaCache);
    }

    private static ChatMessageModel Message(int id) => new()
    {
        Id = id, ChatId = Chat.ChatId, SenderUserId = 1, SentTime = DateTime.UtcNow,
        Images = [], Video = [], Files = [], Reactions = [], UsersIds = []
    };

    [Fact]
    public async Task SavedSettings_TakeEffectAtStart()
    {
        _settings.Value = new StorageSettings { ChatCacheMessagesPerChat = 500, MediaCacheLimitMegabytes = 128 };

        await _service.ApplyAsync();

        Assert.Equal(500, _chatCache.MaxMessages);
        Assert.Equal(128L * 1024 * 1024, _mediaCache.SizeLimit);
        Assert.True(_caches.IsEnabled);
    }

    [Fact]
    public async Task SavingSettings_PutsThemIntoEffectAtOnce()
    {
        await _service.SaveSettingsAsync(new StorageSettings { ChatCacheMessagesPerChat = 5000, MediaCacheLimitMegabytes = 2048 });

        Assert.Equal(5000, _chatCache.MaxMessages);
        Assert.Equal(2048L * 1024 * 1024, _mediaCache.SizeLimit);
        Assert.Equal(5000, (await _service.GetSettingsAsync()).ChatCacheMessagesPerChat);
    }

    [Fact]
    public async Task TurningTheKeepingOff_RemovesTheMessagesAndStopsWritingThem()
    {
        var cache = _caches.Open(Chat.CoordinatorInstanceId, Chat.ChatId, 1);
        cache.Replace([Message(1), Message(2)], 2, true);
        Assert.Equal(2, (await cache.LoadNewestAsync(10))!.Messages.Count);

        await _service.SaveSettingsAsync(new StorageSettings { IsChatCacheEnabled = false });

        Assert.Equal(0, (await _service.GetUsageAsync()).ChatMessages);

        // The chat that was open goes on without its cache
        cache.Replace([Message(3)], 3, true);
        Assert.Null(await cache.LoadNewestAsync(10));
        Assert.Empty(_chatCache.Tails);

        await _service.SaveSettingsAsync(new StorageSettings { IsChatCacheEnabled = true });
        cache.Replace([Message(4)], 4, true);
        Assert.Single((await cache.LoadNewestAsync(10))!.Messages);
    }

    [Fact]
    public async Task Usage_AndClearing()
    {
        _caches.Open(Chat.CoordinatorInstanceId, Chat.ChatId, 1).Replace([Message(1), Message(2)], 2, true);
        await _caches.Open(Chat.CoordinatorInstanceId, Chat.ChatId, 1).LoadNewestAsync(1);
        _mediaCache.Size = 700;

        Assert.Equal(new StorageUsage(2, 20, 700), await _service.GetUsageAsync());

        await _service.ClearChatCacheAsync();
        await _service.ClearMediaCacheAsync();

        Assert.Equal(new StorageUsage(0, 0, 0), await _service.GetUsageAsync());
    }

    private sealed class MemorySettings : IStorageSettingsRepository
    {
        public StorageSettings Value { get; set; } = new();
        public Task<StorageSettings> GetAsync() => Task.FromResult(Value);
        public Task SaveAsync(StorageSettings settings) { Value = settings; return Task.CompletedTask; }
    }

    private sealed class MemoryMediaCache : IMediaCache
    {
        public long Size { get; set; }
        public long SizeLimit { get; set; }
        public Task<long> GetSizeAsync() => Task.FromResult(Size);
        public Task ClearAsync() { Size = 0; return Task.CompletedTask; }
        public string? Find(string coordinatorInstanceId, int imageId, bool preview) => null;
        public Task<string?> StoreAsync(string coordinatorInstanceId, int imageId, bool preview, Func<Stream, Task<bool>> write) =>
            Task.FromResult<string?>(null);
        public string? FindAvatar(string coordinatorInstanceId, string avatarId) => null;
        public Task<string?> StoreAvatarAsync(string coordinatorInstanceId, string avatarId, Func<Stream, Task<bool>> write) =>
            Task.FromResult<string?>(null);
    }

    /// <summary>Keeps what it is given, without the rules of a tail; a message counts as ten bytes.</summary>
    private sealed class MemoryChatCache : IChatCacheRepository
    {
        public Dictionary<ChatCacheKey, List<ChatMessageModel>> Tails { get; } = new();
        public int MaxMessages { get; set; } = 2000;

        public Task<(int Messages, long Bytes)> GetUsageAsync()
        {
            var count = Tails.Values.Sum(tail => tail.Count);
            return Task.FromResult((count, count * 10L));
        }

        public Task RemoveAllAsync() { Tails.Clear(); return Task.CompletedTask; }

        public Task<CachedChatPage?> GetNewestAsync(ChatCacheKey chat, int take) =>
            Task.FromResult(Tails.TryGetValue(chat, out var tail) ? new CachedChatPage(tail.TakeLast(take).ToList(), 0, true) : null);

        public Task<CachedChatPage?> GetBeforeAsync(ChatCacheKey chat, int beforeMessageId, int take) => GetNewestAsync(chat, take);

        public Task ReplaceAsync(ChatCacheKey chat, IReadOnlyList<ChatMessageModel> messages, long revision, bool reachesStart)
        {
            Tails[chat] = messages.ToList();
            return Task.CompletedTask;
        }

        public Task AddOlderAsync(ChatCacheKey chat, int beforeMessageId, IReadOnlyList<ChatMessageModel> messages, bool reachesStart) => Task.CompletedTask;
        public Task ApplyAsync(ChatCacheKey chat, IReadOnlyList<ChatMessageModel> changed, IReadOnlyList<int> deletedIds, long revision) => Task.CompletedTask;
        public Task RemoveAsync(ChatCacheKey chat) { Tails.Remove(chat); return Task.CompletedTask; }
        public Task RemoveExceptAsync(string coordinatorInstanceId, IReadOnlyCollection<int> existingChatIds) => Task.CompletedTask;
    }
}
