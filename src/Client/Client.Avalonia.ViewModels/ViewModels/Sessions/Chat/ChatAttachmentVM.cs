using System.Threading.Tasks;
using Avalonia.Media;
using Client.Application.Chat;
using Client.Services;
using ReactiveUI;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// A file attached to the message being typed, shown as a card above the input. An image goes as one and
/// is shown in the message; any other file goes as it is, to be downloaded.
/// </summary>
public class ChatAttachmentVM : ReactiveObject
{
    // The card is small; the picture is decoded no larger than it is drawn at double density
    private const int ThumbnailSide = 128;

    public string FileName { get; }
    public string? Key { get; }
    public long Size { get; }
    public string SizeText { get; }

    public ChatMediaSource Source { get; }

    /// <summary>
    /// Sent as an image, shown in the message. A file that turns out not to be one is sent as a file.
    /// </summary>
    public bool IsImage
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsFile));
        }
    }

    public bool IsFile => !IsImage;

    /// <summary>
    /// How far the upload got. Kept between attempts to send, so the next one continues it.
    /// </summary>
    public ChatImageUploadState Upload { get; private set; } = new();

    /// <inheritdoc cref="Upload"/>
    public ChatFileUploadState FileUpload { get; private set; } = new();

    public IImage? Thumbnail
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsUploading
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>The share of the file the server holds, from 0 to 1.</summary>
    public double Progress
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>Why the file did not go, shown on its card.</summary>
    public string? Error
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => Error != null;

    public ChatAttachmentVM(PickedFile file, bool isImage, IChatImageService images)
    {
        FileName = file.Name;
        Key = file.Key;
        Size = file.Size;
        SizeText = Services.SizeText.Format(file.Size);
        Source = new ChatMediaSource(file.Name, file.Size, file.Open);
        IsImage = isImage;

        if (isImage)
            _ = LoadThumbnail(images);
    }

    /// <summary>
    /// Forgets what was uploaded: the server does not hold it anymore.
    /// </summary>
    public void ResetUpload()
    {
        Upload = new ChatImageUploadState();
        FileUpload = new ChatFileUploadState();
        Progress = 0;
    }

    /// <summary>
    /// The picture could not be read as an image the chat shows: it goes as a file instead.
    /// </summary>
    public void SendAsFile()
    {
        IsImage = false;
        Thumbnail = null;
        Progress = 0;
    }

    private async Task LoadThumbnail(IChatImageService images) =>
        Thumbnail = await images.DecodeAsync(Source.Open, ThumbnailSide);
}
