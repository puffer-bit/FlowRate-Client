using System;
using System.Threading.Tasks;
using Avalonia.Media;
using Client.Application.Coordinator;
using Client.Core.Chat;
using Client.Services;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// An image of a message: its reduced copy in a tile, opened in full by a click.
/// </summary>
public class ChatImageVM : ReactiveObject
{
    // The box an only image of a message is fitted into, and the side of a tile among several
    private const double SingleMaxSide = 320;
    private const double SingleMinSide = 96;
    private const double TileSide = 152;

    // A GIF moves only in the file itself, which is downloaded for the tile up to this size
    private const long MaxAnimatedSize = 16 * 1024 * 1024;

    public ChatImageContent Image { get; }
    public double TileWidth { get; }
    public double TileHeight { get; }

    /// <summary>
    /// Null until the reduced copy is loaded, and again once no view shows the image. An animated
    /// image is still until its file is downloaded.
    /// </summary>
    public IImage? Preview
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsLoaded));
        }
    }

    public bool IsLoaded => Preview != null;

    /// <summary>The reduced copy could not be had; the next time the image is shown it is tried again.</summary>
    public bool IsFailed
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> OpenCommand { get; }

    private readonly ICoordinatorSession _session;
    private readonly IChatImageService _images;
    private int _viewers;
    private bool _isLoading;

    public ChatImageVM(ChatImageContent image, bool isOnlyImage, ICoordinatorSession session,
        IChatImageService images, Action<ChatImageVM> open)
    {
        Image = image;
        _session = session;
        _images = images;
        (TileWidth, TileHeight) = isOnlyImage ? FitSingle(image.Width, image.Height) : (TileSide, TileSide);
        OpenCommand = ReactiveCommand.Create(() => open(this));
    }

    /// <summary>
    /// A view began to show the image. The bitmap is held only while one does, so a long history
    /// does not keep every picture it scrolled through in memory.
    /// </summary>
    public void Show()
    {
        if (++_viewers == 1 && Preview == null)
            _ = Load();
    }

    public void Hide()
    {
        if (_viewers > 0 && --_viewers == 0)
            Release(Preview);
    }

    // The bitmap is native memory the collector does not count, so it is freed at once rather than
    // when the collector comes around to it; a render still drawing it holds a reference of its own
    private void Release(IImage? picture)
    {
        if (Preview == picture)
            Preview = null;
        (picture as IDisposable)?.Dispose();
    }

    private async Task Load()
    {
        if (_isLoading)
            return;

        _isLoading = true;
        try
        {
            IsFailed = false;
            var bitmap = await _images.LoadPreviewAsync(_session, Image.Id);
            IsFailed = bitmap == null;

            // Scrolled away while it loaded
            if (_viewers == 0)
            {
                Release(bitmap);
                return;
            }

            Preview = bitmap;

            if (bitmap != null && IsAnimatable && await _images.LoadAnimationAsync(_session, Image.Id) is { } animation)
            {
                if (_viewers == 0)
                {
                    Release(animation);
                    return;
                }

                // The still copy was let go of already if the image was hidden and shown again meanwhile
                var still = Preview;
                Preview = animation;
                Release(still);
            }
        }
        finally
        {
            _isLoading = false;
        }
    }

    private bool IsAnimatable =>
        Image.MimeType == "image/gif" && Image.Size <= MaxAnimatedSize;

    private static (double Width, double Height) FitSingle(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return (SingleMaxSide, SingleMaxSide);

        var scale = Math.Min(1, Math.Min(SingleMaxSide / width, SingleMaxSide / height));
        // A very long or very narrow picture still gets a tile that can be seen and clicked
        return (Math.Max(SingleMinSide, Math.Round(width * scale)), Math.Max(SingleMinSide, Math.Round(height * scale)));
    }
}
