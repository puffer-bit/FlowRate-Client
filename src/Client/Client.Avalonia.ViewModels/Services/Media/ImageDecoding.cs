using System;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Services;

internal static class ImageDecoding
{
    // Shared by chat images and avatars, including their conversion to Avalonia bitmaps.
    private static readonly SemaphoreSlim Slots = new(2, 2);

    public static Task<T> RunAsync<T>(Func<T> decode, CancellationToken cancellationToken = default) =>
        RunAsync(() => Task.FromResult(decode()), cancellationToken);

    public static Task<T> RunAsync<T>(Func<Task<T>> decode, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            await Slots.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await decode().ConfigureAwait(false);
            }
            finally
            {
                Slots.Release();
            }
        }, cancellationToken);
}
