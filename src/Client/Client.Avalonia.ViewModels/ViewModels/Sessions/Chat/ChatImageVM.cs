using System;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
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

    public ChatImageContent Image { get; }
    public double TileWidth { get; }
    public double TileHeight { get; }

    /// <summary>
    /// Null until the reduced copy is loaded, and again once no view shows the image.
    /// </summary>
    public Bitmap? Preview
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
            Preview = null;
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

            // Scrolled away while it loaded
            if (_viewers > 0)
                Preview = bitmap;
            IsFailed = bitmap == null;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private static (double Width, double Height) FitSingle(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return (SingleMaxSide, SingleMaxSide);

        var scale = Math.Min(1, Math.Min(SingleMaxSide / width, SingleMaxSide / height));
        // A very long or very narrow picture still gets a tile that can be seen and clicked
        return (Math.Max(SingleMinSide, Math.Round(width * scale)), Math.Max(SingleMinSide, Math.Round(height * scale)));
    }
}
