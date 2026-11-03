using Client.Application.Chat;
using Client.Application.Ports.Repositories;
using Client.Core.Chat;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Client.Application.Tests;

public class ChatCacheTests
{
    [Fact]
    public async Task SynchronousRepositoryWorkDoesNotBlockCaller()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var returned = new TaskCompletionSource<Task<CachedChatPage?>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new Repository
        {
            Read = () =>
            {
                entered.SetResult();
                release.Wait();
                return Task.FromResult<CachedChatPage?>(new([], 1, true));
            }
        };
        var cache = Create(repository);
        // A dedicated caller stands in for the UI thread; the repository blocks until the test releases it.
        var caller = new Thread(() => returned.SetResult(cache.LoadNewestAsync(30))) { IsBackground = true };
        caller.Start();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var read = await returned.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(read.IsCompleted);
        }
        finally
        {
            release.Set();
            caller.Join(TimeSpan.FromSeconds(10));
            await (await returned.Task).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task QueuedReadsWaitForWritesAndContinueAfterFailure()
    {
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readCount = 0;
        var repository = new Repository
        {
            Write = async () =>
            {
                writing.SetResult();
                await release.Task;
                throw new IOException("Storage unavailable");
            },
            Read = () =>
            {
                Interlocked.Increment(ref readCount);
                return Task.FromResult<CachedChatPage?>(new([], 2, true));
            }
        };
        var cache = Create(repository);
        cache.Replace([], 1, true);
        var read = cache.LoadNewestAsync(30);
        try
        {
            await writing.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(read.IsCompleted);
            Assert.Equal(0, Volatile.Read(ref readCount));
        }
        finally
        {
            release.SetResult();
        }

        Assert.Equal(2, (await read.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken))!.Revision);
        Assert.Equal(1, readCount);
    }

    private static ChatCache Create(Repository repository) =>
        new(repository, NullLogger.Instance, new ChatCacheKey("instance", 7), 1, () => true);

    private sealed class Repository : IChatCacheRepository
    {
        public Func<Task<CachedChatPage?>> Read { get; init; } = () => Task.FromResult<CachedChatPage?>(null);
        public Func<Task> Write { get; init; } = () => Task.CompletedTask;
        public int MaxMessages { get; set; }
        public Task<CachedChatPage?> GetNewestAsync(ChatCacheKey chat, int take) => Read();
        public Task<CachedChatPage?> GetBeforeAsync(ChatCacheKey chat, int beforeMessageId, int take) => Read();
        public Task ReplaceAsync(ChatCacheKey chat, IReadOnlyList<ChatMessageModel> messages, long revision,
            bool reachesStart) => Write();
        public Task AddOlderAsync(ChatCacheKey chat, int beforeMessageId, IReadOnlyList<ChatMessageModel> messages,
            bool reachesStart) => Write();
        public Task ApplyAsync(ChatCacheKey chat, IReadOnlyList<ChatMessageModel> changed,
            IReadOnlyList<int> deletedIds, long revision) => Write();
        public Task<(int Messages, long Bytes)> GetUsageAsync() => Task.FromResult((0, 0L));
        public Task RemoveAllAsync() => Task.CompletedTask;
        public Task RemoveAsync(ChatCacheKey chat) => Task.CompletedTask;
        public Task RemoveExceptAsync(string coordinatorInstanceId, IReadOnlyCollection<int> existingChatIds) =>
            Task.CompletedTask;
    }
}
