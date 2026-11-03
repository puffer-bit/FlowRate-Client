using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Client.Application.Coordinator;
using Client.Application.Ports;
using Client.Avalonia.Localization;
using Client.Core.Chat;
using Client.Services;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// A file of a message: the first click caches it, a later click opens it, and its menu offers Save as.
/// </summary>
public class ChatFileVM : ReactiveObject
{
    public ChatFileContent File { get; }
    public string FileName => File.FileName;
    public string SizeText { get; }

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

    /// <param name="pickSaveTarget">
    /// Asks where to save: it is given the suggested file name and returns the stream to write into,
    /// or null when the user changed their mind.
    /// </param>
    public ChatFileVM(ChatFileContent file, ICoordinatorSession session, Func<string, Task<Stream?>> pickSaveTarget,
        IMediaCache cache, Func<string, Task<bool>> openFile)
    {
        File = file;
        _session = session;
        _pickSaveTarget = pickSaveTarget;
        _cache = cache;
        _openFile = openFile;
        _coordinator = session.GetCurrentUser().CoordinatorInstanceId;
        IsDownloaded = _cache.FindFile(_coordinator, File.Id, FileName) != null;
        SizeText = Services.SizeText.Format(file.Size);

        DownloadCommand = ReactiveCommand.CreateFromTask(DownloadOrCancel);
        // The command finishes immediately so another click can cancel the ongoing download.
        OpenCommand = ReactiveCommand.Create(() => { _ = OpenOrCancel(); });
    }

    private async Task OpenOrCancel()
    {
        if (_download is { } running)
        {
            running.Cancel();
            return;
        }

        Error = null;
        var kept = _cache.FindFile(_coordinator, File.Id, FileName);
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
        if (_cache.FindFile(_coordinator, File.Id, FileName) is { } kept)
        {
            IsDownloaded = true;
            return (MediaTransferResult.NoError, kept);
        }

        IsDownloaded = false;

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
