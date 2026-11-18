using System;
using System.Threading.Tasks;
using Avalonia.Media;
using Client.Application.Coordinator;
using Client.Core.Chat;
using Client.Services;
using ReactiveUI;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// The card of a link under a message or above the input: the site, the title, the description and
/// the picture of the page. A wide picture goes under the text, a small one beside it.
/// </summary>
public sealed class ChatLinkPreviewVM : ReactiveObject, IShownImage
{
    // The width a wide picture is fitted into, and the bounds of its height
    private const double LargeImageWidth = 320;
    private const double LargeImageMinHeight = 80;
    private const double LargeImageMaxHeight = 240;

    public ChatLinkPreview Preview { get; }

    public Uri? Url { get; }
    public string SiteName { get; }
    public string? Title => Preview.Title;
    public string? Description => Preview.Description;
    public bool HasTitle => !string.IsNullOrEmpty(Preview.Title);
    public bool HasDescription => !string.IsNullOrEmpty(Preview.Description);

    public bool HasLargeImage => Preview.HasImage && Preview.IsLargeImage;
    public bool HasSmallImage => Preview.HasImage && !Preview.IsLargeImage;

    /// <summary>The height a wide picture takes at <see cref="LargeImageWidth"/>, so the card keeps its size while it loads.</summary>
    public double LargeImageHeight { get; }

    /// <summary>Asks before the link is opened, as for links in the text.</summary>
    public Func<Uri, Task<bool>>? ConfirmOpen { get; init; }

    /// <summary>Null until the picture is loaded, and again once no view shows it.</summary>
    public IImage? Image
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsImageLoaded));
        }
    }

    public bool IsImageLoaded => Image != null;

    private readonly ICoordinatorSession _session;
    private readonly IChatImageService _images;
    private int _viewers;
    private bool _isLoading;

    public ChatLinkPreviewVM(ChatLinkPreview preview, ICoordinatorSession session, IChatImageService images)
    {
        Preview = preview;
        _session = session;
        _images = images;
        Url = Uri.TryCreate(preview.Url, UriKind.Absolute, out var url) ? url : null;
        SiteName = !string.IsNullOrWhiteSpace(preview.SiteName) ? preview.SiteName : Url?.Host ?? preview.Url;
        LargeImageHeight = preview is { ImageWidth: > 0, ImageHeight: > 0 }
            ? Math.Clamp(Math.Round(LargeImageWidth * preview.ImageHeight / preview.ImageWidth), LargeImageMinHeight,
                LargeImageMaxHeight)
            : LargeImageMaxHeight;
    }

    public void Show()
    {
        if (++_viewers == 1 && Image == null && Preview.HasImage)
            _ = Load();
    }

    public void Hide()
    {
        if (_viewers > 0 && --_viewers == 0)
            Release(Image);
    }

    private void Release(IImage? picture)
    {
        if (Image == picture)
            Image = null;
        (picture as IDisposable)?.Dispose();
    }

    private async Task Load()
    {
        if (_isLoading)
            return;

        _isLoading = true;
        try
        {
            var bitmap = await _images.LoadLinkPreviewImageAsync(_session, Preview.Id);
            if (_viewers == 0)
            {
                Release(bitmap);
                return;
            }
            Image = bitmap;
        }
        finally
        {
            _isLoading = false;
        }
    }
}
