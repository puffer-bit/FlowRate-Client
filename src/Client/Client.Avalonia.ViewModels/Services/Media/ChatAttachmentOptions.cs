using System;
using System.Threading;
using System.Threading.Tasks;
using Client.Application.Ports;
using Client.Application.Ports.Repositories;
using Client.Core.Chat;
using Microsoft.Extensions.Logging;

namespace Client.Services;

/// <summary>
/// What is done to images attached from now on, shared by every chat and kept between launches, and the
/// preparer that does it.
/// </summary>
public sealed partial class ChatAttachmentOptions
{
    private readonly IChatAttachmentPreferencesRepository? _repository;
    private readonly ILogger<ChatAttachmentOptions>? _logger;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private Task? _loading;

    public IImagePreparer Preparer { get; }

    public ChatAttachmentPreferences Defaults { get; private set; } = new();

    /// <summary>Raised on the thread that changed or loaded them.</summary>
    public event Action? DefaultsChanged;

    public ChatAttachmentOptions(IImagePreparer preparer, IChatAttachmentPreferencesRepository? repository = null,
        ILogger<ChatAttachmentOptions>? logger = null)
    {
        Preparer = preparer;
        _repository = repository;
        _logger = logger;
    }

    public Task LoadAsync() => _loading ??= LoadCoreAsync();

    private async Task LoadCoreAsync()
    {
        if (_repository == null)
            return;
        try
        {
            Defaults = await _repository.GetAsync();
            DefaultsChanged?.Invoke();
        }
        catch (Exception exception)
        {
            if (_logger != null)
                LogLoadFailed(_logger, exception);
        }
    }

    public async Task SetDefaultsAsync(ChatAttachmentPreferences defaults)
    {
        if (defaults == Defaults)
            return;
        Defaults = defaults;
        DefaultsChanged?.Invoke();
        if (_repository == null)
            return;

        // One write at a time, so an older choice cannot overwrite a newer one
        await _saveLock.WaitAsync();
        try { await _repository.SaveAsync(Defaults); }
        catch (Exception exception)
        {
            if (_logger != null)
                LogSaveFailed(_logger, exception);
        }
        finally { _saveLock.Release(); }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not load what is done to attached images.")]
    private static partial void LogLoadFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not save what is done to attached images.")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception);
}
