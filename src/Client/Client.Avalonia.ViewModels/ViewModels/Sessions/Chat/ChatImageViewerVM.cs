using System;
using System.IO;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Client.Application.Coordinator;
using Client.Avalonia.Localization;
using Client.Core.Chat;
using Client.Services;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// An image of a message opened over the chat: the reduced copy at once, then the image itself
/// when it has been downloaded.
/// </summary>
public class ChatImageViewerVM : ReactiveObject, IDisposable
{
    // Larger images are shown reduced to this; saving writes the file as it was sent
    private const int MaxShownSide = 4096;

    public string FileName { get; }

    public IImage? Picture
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>True until the image itself is shown or has failed to load.</summary>
    public bool IsLoading
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>The share of the image received, from 0 to 1.</summary>
    public double Progress
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

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

    /// <summary>The image file is here and can be saved.</summary>
    public bool CanSave
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// Asks the view where to save: it is given the suggested file name and returns the stream
    /// to write into, or null when the user changed their mind.
    /// </summary>
    public Interaction<string, Stream?> PickSaveTarget { get; } = new();

    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CloseCommand { get; }

    private readonly ChatImageVM _tile;
    private readonly ChatImageContent _image;
    private readonly ICoordinatorSession _session;
    private readonly IChatImageService _images;
    private readonly CancellationTokenSource _closed = new();
    private string? _path;

    public ChatImageViewerVM(ChatImageVM image, ICoordinatorSession session, IChatImageService images, Action close)
    {
        // The reduced copy is shown until the image itself comes, so it is held like a tile holds it
        _tile = image;
        _tile.Show();
        _image = image.Image;
        _session = session;
        _images = images;
        FileName = _image.FileName;
        Picture = image.Preview;

        SaveCommand = ReactiveCommand.CreateFromTask(Save, this.WatchProperty(nameof(CanSave), viewer => viewer.CanSave));
        CloseCommand = ReactiveCommand.Create(close);

        _ = Load();
    }

    public void Dispose()
    {
        _closed.Cancel();
        _closed.Dispose();

        // The image itself is the viewer's own; the reduced copy belongs to the tile
        if (Picture != _tile.Preview)
            (Picture as IDisposable)?.Dispose();
        Picture = null;
        _tile.Hide();
    }

    private async Task Load()
    {
        var token = _closed.Token;
        IsLoading = true;
        try
        {
            var size = Math.Max(_image.Size, 1);
            var (result, path) = await _images.FetchOriginalAsync(_session, _image.Id,
                new Progress<long>(received => Progress = Math.Clamp((double)received / size, 0, 1)), token);
            if (token.IsCancellationRequested)
                return;

            if (result != MediaTransferResult.NoError || path == null)
            {
                Error = Strings.Get(result == MediaTransferResult.NotFound ? "chat.image.gone" : "chat.image.loadFailed");
                return;
            }

            _path = path;
            CanSave = true;

            var bitmap = await _images.DecodeAsync(() => Task.FromResult<Stream>(File.OpenRead(path)), MaxShownSide);
            if (token.IsCancellationRequested)
            {
                (bitmap as IDisposable)?.Dispose();
                return;
            }

            if (bitmap != null)
                Picture = bitmap;
            else if (Picture == null)
                Error = Strings.Get("chat.image.loadFailed");
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task Save()
    {
        if (_path is not { } path)
            return;

        var target = await PickSaveTarget.Handle(FileName);
        if (target == null)
            return;

        try
        {
            await using (target)
            await using (var source = File.OpenRead(path))
                await source.CopyToAsync(target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Error = Strings.Get("chat.image.saveFailed");
        }
    }
}
