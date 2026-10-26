using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Client.Application.Coordinator;
using Client.Avalonia.Localization;
using Client.Core.Chat;
using Client.Services;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// A file of a message that is not shown: its name and size on a card, saved where the user picks by a click.
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
        }
    }

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

    public string ActionToolTip => Strings.Get(IsDownloading ? "chat.file.cancel" : "chat.file.download");

    /// <summary>Starts the download, or stops the one in progress.</summary>
    public ReactiveCommand<RxVoid, RxVoid> DownloadCommand { get; }

    private readonly ICoordinatorSession _session;
    private readonly Func<string, Task<Stream?>> _pickSaveTarget;
    private CancellationTokenSource? _download;

    /// <param name="pickSaveTarget">
    /// Asks where to save: it is given the suggested file name and returns the stream to write into,
    /// or null when the user changed their mind.
    /// </param>
    public ChatFileVM(ChatFileContent file, ICoordinatorSession session, Func<string, Task<Stream?>> pickSaveTarget)
    {
        File = file;
        _session = session;
        _pickSaveTarget = pickSaveTarget;
        SizeText = Services.SizeText.Format(file.Size);

        DownloadCommand = ReactiveCommand.CreateFromTask(DownloadOrCancel);
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
                var size = Math.Max(File.Size, 1);
                result = await _session.DownloadChatFile(File.Id, target,
                    new Progress<long>(received => Progress = Math.Clamp((double)received / size, 0, 1)),
                    cancellation.Token);
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
