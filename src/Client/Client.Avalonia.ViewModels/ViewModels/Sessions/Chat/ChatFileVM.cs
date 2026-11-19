using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Client.Application.Chat;
using Client.Application.Coordinator;
using Client.Application.Ports;
using Avalonia.Media;
using Client.Avalonia.Localization;
using Client.Core.Chat;
using Client.Services;
using ReactiveUI;

using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// A file of a message: the first click caches it, a later click opens it, and its menu offers Save as.
/// </summary>
public class ChatFileVM : ReactiveObject, IShownImage
{
    // The width of the inline player and the bounds of its picture's height
    private const double PlayerMaxWidth = 400;
    private const double PlayerMaxHeight = 400;

    public ICoordinatorSession CoordinatorSession => _session;
    public ChatFileContent File { get; }
    public string FileName => File.FileName;
    public string SizeText { get; }
    public bool IsVideo => Client.Core.Media.MediaRuntime.IsEnabled && Path.GetExtension(FileName).ToLowerInvariant() is ".mp4" or ".mkv" or ".webm" or ".mov" or ".avi";
    public bool IsMedia => IsAudio || IsVideo;
    public bool IsAudio => Client.Core.Media.MediaRuntime.IsEnabled && Path.GetExtension(FileName).ToLowerInvariant() is ".mp3" or ".wav";
    public (string Coordinator, int FileId) PlaybackKey => (_coordinator, File.Id);

    /// <summary>
    /// The height of the picture of the inline player, by the shape of the video where the server knows it,
    /// so the player keeps its size while the frame loads and once it plays.
    /// </summary>
    public double PlayerHeight { get; }
    public double PlayerWidth { get; }
    public double PlayerCardWidth => Math.Clamp(PlayerWidth + 12, 240, PlayerMaxWidth);

    /// <summary>The length of the video as the server read it, in seconds; 0 when unknown.</summary>
    public double KnownDuration => File.Video?.Duration?.TotalSeconds ?? 0;

    /// <summary>The frame the video is shown with until it plays; null until loaded, and again once no view shows it.</summary>
    public IImage? Poster
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(HasPoster));
        }
    }

    public bool HasPoster => Poster != null;

    public bool IsDownloading
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(ActionToolTip));
            this.RaisePropertyChanged(nameof(ShowDownloadIcon));
            this.RaisePropertyChanged(nameof(ShowDownloadedIcon));
        }
    }

    public bool IsDownloaded
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(ActionToolTip));
            this.RaisePropertyChanged(nameof(ShowDownloadIcon));
            this.RaisePropertyChanged(nameof(ShowDownloadedIcon));
        }
    }

    public bool ShowDownloadIcon => !IsDownloading && !IsDownloaded;
    public bool ShowDownloadedIcon => !IsDownloading && IsDownloaded;

    /// <summary>The share of the file received, from 0 to 1.</summary>
    public double Progress
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>Saved by the last download, until another one starts.</summary>
    public bool IsSaved
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>Why the last download did not finish, shown on the card.</summary>
    public string? Error
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => Error != null;

    public string ActionToolTip => Strings.Get(IsDownloading ? "chat.file.cancel"
        : IsDownloaded ? "chat.file.open" : "chat.file.download");

    public ReactiveCommand<RxVoid, RxVoid> OpenCommand { get; }

    /// <summary>Starts the download, or stops the one in progress.</summary>
    public ReactiveCommand<RxVoid, RxVoid> DownloadCommand { get; }

    private readonly ICoordinatorSession _session;
    private readonly Func<string, Task<Stream?>> _pickSaveTarget;
    private readonly IMediaCache _cache;
    private readonly Func<string, Task<bool>> _openFile;
    private readonly string _coordinator;
    private CancellationTokenSource? _download;
    private bool _isFindingFile;
    private readonly IChatImageService? _images;
    private int _viewers;
    private bool _isLoadingPoster;

    /// <param name="pickSaveTarget">
    /// Asks where to save: it is given the suggested file name and returns the stream to write into,
    /// or null when the user changed their mind.
    /// </param>
    /// <param name="images">Loads the frame of a video; without it a video shows none.</param>
    public ChatFileVM(ChatFileContent file, ICoordinatorSession session, Func<string, Task<Stream?>> pickSaveTarget,
        IMediaCache cache, Func<string, Task<bool>> openFile, IChatImageService? images = null)
    {
        File = file;
        _images = images;
        var width = file.Video is { Width: > 0 } ? file.Video.Width : 400;
        var height = file.Video is { Height: > 0 } ? file.Video.Height : 225;
        var scale = Math.Min(1, Math.Min(PlayerMaxWidth / width, PlayerMaxHeight / height));
        PlayerWidth = Math.Round(width * scale);
        PlayerHeight = Math.Round(height * scale);
        _session = session;
        _pickSaveTarget = pickSaveTarget;
        _cache = cache;
        _openFile = openFile;
        _coordinator = session.GetCurrentUser().CoordinatorInstanceId;
        SizeText = Services.SizeText.Format(file.Size);

        DownloadCommand = ReactiveCommand.CreateFromTask(DownloadOrCancel);
        // The command finishes immediately so another click can cancel the ongoing download.
        OpenCommand = ReactiveCommand.Create(() => { _ = OpenOrCancel(); });
        _ = CheckDownloadedAsync();
    }

    public void Show()
    {
        if (++_viewers == 1 && Poster == null && IsVideo && File.Video?.HasPreview == true)
            _ = LoadPosterAsync();
    }

    public void Hide()
    {
        if (_viewers > 0 && --_viewers == 0)
            ReleasePoster(Poster);
    }

    private void ReleasePoster(IImage? picture)
    {
        if (Poster == picture)
            Poster = null;
        (picture as IDisposable)?.Dispose();
    }

    private async Task LoadPosterAsync()
    {
        if (_isLoadingPoster || _images == null)
            return;

        _isLoadingPoster = true;
        try
        {
            var bitmap = await _images.LoadVideoPreviewAsync(_session, File.Id);
            if (_viewers == 0)
            {
                ReleasePoster(bitmap);
                return;
            }
            Poster = bitmap;
        }
        finally
        {
            _isLoadingPoster = false;
        }
    }

    private Task<string?> FindCachedFileAsync() =>
        Task.Run(() => _cache.FindFile(_coordinator, File.Id, FileName));

    private async Task CheckDownloadedAsync()
    {
        var path = await FindCachedFileAsync();
        // A download may have started or finished while the initial disk lookup was pending.
        if (_download == null && !IsDownloaded)
            IsDownloaded = path != null;
    }

    /// <summary>
    /// Opens the attachment for a player: the cached file when it is all there, otherwise as it downloads, so it
    /// plays before it has arrived. Called on the UI thread.
    /// </summary>
    /// <returns>Null when it could not be opened; <see cref="Error"/> then says why.</returns>
    public async Task<ChatPlaybackSource?> OpenPlaybackAsync(CancellationToken cancellationToken)
    {
        Error = null;
        if (await FindCachedFileAsync() is { } kept)
        {
            IsDownloaded = true;
            return ChatPlaybackSource.FromFile(kept, FileName);
        }
        cancellationToken.ThrowIfCancellationRequested();

        var partial = await Task.Run(() => _cache.OpenPartialFile(_coordinator, File.Id, FileName, File.Size));
        if (partial == null)
            return await GetPlaybackPathAsync(cancellationToken) is { } path ? ChatPlaybackSource.FromFile(path, FileName) : null;
        if (cancellationToken.IsCancellationRequested)
        {
            partial.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
        }

        Progress = (double)partial.ChunksPresent / partial.ChunkCount;
        return ChatPlaybackSource.FromDownload(new StreamingDownload(partial, FetchRangeAsync,
            new Progress<double>(ReportStreamed)), FileName);
    }

    /// <summary>Tells why a download opened by <see cref="OpenPlaybackAsync"/> stopped.</summary>
    public void ReportPlaybackFailure(MediaTransferResult result) =>
        Error = Strings.Get(result == MediaTransferResult.NotFound ? "chat.file.gone" : "chat.file.downloadFailed");

    private void ReportStreamed(double share)
    {
        if (_download == null)
            Progress = share;
        if (share >= 1)
            IsDownloaded = true;
    }

    private Task<MediaTransferResult> FetchRangeAsync(long offset, long length, Stream destination,
        CancellationToken cancellationToken) =>
        _session.DownloadChatFileRange(File.Id, offset, length, destination, cancellationToken);

    /// <summary>Downloads media into the same cache used by file attachments.</summary>
    public async Task<string?> GetPlaybackPathAsync(CancellationToken cancellationToken)
    {
        if (_download != null)
            return null;

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _download = cancellation;
        Error = null;
        Progress = 0;
        IsDownloading = true;
        try
        {
            var (result, path) = await FetchAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (path != null)
            {
                Progress = 1;
                return path;
            }
            if (result != MediaTransferResult.Canceled)
                Error = Strings.Get(result == MediaTransferResult.NotFound ? "chat.file.gone" : "chat.file.downloadFailed");
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Error = Strings.Get("chat.file.downloadFailed");
            return null;
        }
        finally
        {
            _download = null;
            IsDownloading = false;
        }
    }

    private async Task OpenOrCancel()
    {
        if (_download is { } running)
        {
            running.Cancel();
            return;
        }

        if (_isFindingFile)
            return;

        Error = null;
        string? kept;
        _isFindingFile = true;
        try
        {
            kept = await FindCachedFileAsync();
        }
        finally
        {
            _isFindingFile = false;
        }
        if (_download != null)
            return;

        IsDownloaded = kept != null;
        if (kept != null)
        {
            try
            {
                if (!await _openFile(kept))
                    Error = Strings.Get("chat.file.openFailed");
            }
            catch (UnhandledInteractionException<string, bool>)
            {
                Error = Strings.Get("chat.file.openFailed");
            }
            return;
        }

        using var cancellation = _download = new CancellationTokenSource();
        Error = null;
        Progress = 0;
        IsDownloading = true;
        try
        {
            var (result, path) = await FetchAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (path != null)
            {
                Progress = 1;
                IsDownloaded = true;
            }
            else if (result != MediaTransferResult.Canceled)
                Error = Strings.Get(result == MediaTransferResult.NotFound ? "chat.file.gone" : "chat.file.downloadFailed");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Error = Strings.Get("chat.file.downloadFailed");
        }
        finally
        {
            _download = null;
            IsDownloading = false;
        }
    }

    private async Task<(MediaTransferResult Result, string? Path)> FetchAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (await FindCachedFileAsync() is { } kept)
        {
            IsDownloaded = true;
            return (MediaTransferResult.NoError, kept);
        }

        IsDownloaded = false;

        // Chunk by chunk, so what was played or downloaded before is not fetched again
        if (await Task.Run(() => _cache.OpenPartialFile(_coordinator, File.Id, FileName, File.Size)) is { } partial)
        {
            using var download = new StreamingDownload(partial, FetchRangeAsync,
                new Progress<double>(share => Progress = share), wholeFile: true);
            Progress = download.Progress;
            var outcome = await download.WaitCompleteAsync(cancellationToken);
            var complete = download.CompletePath;
            IsDownloaded = complete != null;
            return (complete != null ? MediaTransferResult.NoError
                : outcome == MediaTransferResult.NoError ? MediaTransferResult.Failed : outcome, complete);
        }

        var result = MediaTransferResult.Failed;
        var size = Math.Max(File.Size, 1);
        var path = await _cache.StoreFileAsync(_coordinator, File.Id, FileName, async stream =>
        {
            result = await _session.DownloadChatFile(File.Id, stream,
                new Progress<long>(received => Progress = Math.Clamp((double)received / size, 0, 1)), cancellationToken);
            return result == MediaTransferResult.NoError;
        });
        IsDownloaded = path != null;
        return (path != null ? MediaTransferResult.NoError
            : result == MediaTransferResult.NoError ? MediaTransferResult.Failed : result, path);
    }

    private async Task DownloadOrCancel()
    {
        if (_download is { } running)
        {
            running.Cancel();
            return;
        }

        var target = await _pickSaveTarget(FileName);
        if (target == null)
            return;

        using var cancellation = _download = new CancellationTokenSource();
        Error = null;
        IsSaved = false;
        Progress = 0;
        IsDownloading = true;
        try
        {
            MediaTransferResult result;
            await using (target)
            {
                var fetched = await FetchAsync(cancellation.Token);
                result = fetched.Result;
                if (fetched.Path is { } path)
                {
                    await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                        81920, FileOptions.Asynchronous);
                    await source.CopyToAsync(target, cancellation.Token);
                }
            }

            switch (result)
            {
                case MediaTransferResult.NoError:
                    Progress = 1;
                    IsSaved = true;
                    break;
                // The user stopped it; nothing went wrong
                case MediaTransferResult.Canceled:
                    break;
                default:
                    Error = Strings.Get(result == MediaTransferResult.NotFound ? "chat.file.gone" : "chat.file.downloadFailed");
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
        // The place picked to save to could not be written
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Error = Strings.Get("chat.file.downloadFailed");
        }
        finally
        {
            _download = null;
            IsDownloading = false;
        }
    }
}
