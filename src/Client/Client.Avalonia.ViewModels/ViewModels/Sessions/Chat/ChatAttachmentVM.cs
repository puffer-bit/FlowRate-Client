using System;
using System.IO;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using Client.Application.Chat;
using Client.Application.Ports;
using Client.Core.Chat;
using Client.Services;
using ReactiveUI;

using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// A file attached to the message being typed, shown as a card above the input. An image goes as one and
/// is shown in the message; any other file goes as it is, to be downloaded. An image may be compressed and
/// stripped of its metadata before it goes, each chosen on its card.
/// </summary>
public class ChatAttachmentVM : ReactiveObject
{
    // The card is small; the picture is decoded no larger than it is drawn at double density
    private const int ThumbnailSide = 128;

    private readonly IImagePreparer? _preparer;
    private ImageCompressionPolicy _policy;

    // The preparation for the current choices; a newer one replaces it
    private CancellationTokenSource? _preparing;
    private Task<PreparedImage?>? _prepared;

    // The choices are set by the constructor before the first preparation
    private bool _isConstructed;

    public string FileName { get; }
    public string? Key { get; }
    public long Size { get; }

    /// <summary>The size of the file, and what it is down to once compressed or stripped.</summary>
    public string SizeText
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

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
            RaiseOptionsChanged();
        }
    }

    public bool IsFile => !IsImage;

    /// <summary>Not animated: compressing would keep only the first frame of an animation.</summary>
    public bool CanCompress
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            RaiseOptionsChanged();
        }
    }

    /// <summary>The server takes only compressed images, so this one cannot go as it is.</summary>
    public bool IsCompressionForced => IsImage && CanCompress && !_policy.AllowOriginals;

    public bool CanToggleCompression => IsImage && CanCompress && !IsCompressionForced && !IsLocked;
    public bool CanToggleMetadata => IsImage && !IsLocked;

    /// <summary>The message with this file is being sent: what goes cannot change meanwhile.</summary>
    public bool IsLocked
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            RaiseOptionsChanged();
        }
    }

    public ReactiveCommand<RxVoid, RxVoid> ToggleCompressionCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ToggleMetadataCommand { get; }

    /// <summary>Reduced and encoded again before it is sent.</summary>
    public bool IsCompressed
    {
        get => field || IsCompressionForced;
        set
        {
            if (field == value || !CanToggleCompression)
                return;
            field = value;
            this.RaisePropertyChanged();
            Prepare();
        }
    }

    /// <summary>Sent without where and when it was taken, the camera and other metadata.</summary>
    public bool IsMetadataStripped
    {
        get;
        set
        {
            if (field == value || !CanToggleMetadata)
                return;
            field = value;
            this.RaisePropertyChanged();
            Prepare();
        }
    }

    public bool IsPreparing
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

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

    /// <param name="preparer">Null to send images as they are.</param>
    public ChatAttachmentVM(PickedFile file, bool isImage, IChatImageService images, IImagePreparer? preparer = null,
        ChatAttachmentPreferences? preferences = null, ImageCompressionPolicy? policy = null)
    {
        FileName = file.Name;
        Key = file.Key;
        Size = file.Size;
        SizeText = Services.SizeText.Format(file.Size);
        Source = new ChatMediaSource(file.Name, file.Size, file.Open);
        _preparer = preparer;
        _policy = policy ?? ImageCompressionPolicy.Default;
        IsImage = isImage;
        // Known for sure once the file is read; a GIF is most likely animated
        CanCompress = isImage && preparer != null && !file.Name.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);

        if (preparer != null && preferences != null)
        {
            IsCompressed = preferences.CompressImages;
            IsMetadataStripped = preferences.StripMetadata;
        }

        ToggleCompressionCommand = ReactiveCommand.Create(() => { IsCompressed = !IsCompressed; },
            this.WatchProperties(nameof(CanToggleCompression)).Select(vm => vm.CanToggleCompression));
        ToggleMetadataCommand = ReactiveCommand.Create(() => { IsMetadataStripped = !IsMetadataStripped; },
            this.WatchProperties(nameof(CanToggleMetadata)).Select(vm => vm.CanToggleMetadata));

        _isConstructed = true;
        if (isImage)
        {
            _ = LoadThumbnail(images);
            Prepare();
        }
    }

    /// <summary>The server asks images to be compressed otherwise.</summary>
    public void ApplyPolicy(ImageCompressionPolicy policy)
    {
        if (policy == _policy)
            return;
        _policy = policy;
        RaiseOptionsChanged();
        Prepare();
    }

    /// <summary>
    /// The file as it is uploaded: compressed or stripped when it was chosen, otherwise as it was picked.
    /// </summary>
    public async Task<ChatMediaSource> GetUploadSourceAsync(CancellationToken cancellationToken)
    {
        if (!IsImage || _prepared is not { } prepared)
            return Source;

        PreparedImage? image;
        try { image = await prepared.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return Source; }

        return image?.Content is { } content
            ? new ChatMediaSource(image.FileName, content.Length,
                () => Task.FromResult<Stream>(new MemoryStream(content, writable: false)))
            : Source;
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
        _preparing?.Cancel();
        _prepared = null;
        IsPreparing = false;
        IsImage = false;
        Thumbnail = null;
        Progress = 0;
        SizeText = Services.SizeText.Format(Size);
    }

    private void Prepare()
    {
        if (_preparer == null || !IsImage || !_isConstructed)
            return;

        _preparing?.Cancel();
        var cancellation = _preparing = new CancellationTokenSource();
        // Whatever was uploaded was the file as it was before
        ResetUpload();

        var preparation = new ImagePreparation(IsCompressed && CanCompress, IsMetadataStripped, _policy);
        if (!preparation.Compress && !preparation.StripMetadata)
        {
            _prepared = null;
            IsPreparing = false;
            SizeText = Services.SizeText.Format(Size);
            return;
        }

        IsPreparing = true;
        var prepared = _prepared = _preparer.PrepareAsync(Source, preparation, cancellation.Token);
        _ = ShowPreparedAsync(prepared, cancellation);
    }

    private async Task ShowPreparedAsync(Task<PreparedImage?> prepared, CancellationTokenSource cancellation)
    {
        PreparedImage? image;
        try { image = await prepared; }
        catch (OperationCanceledException) { return; }
        catch (Exception) { image = null; }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (cancellation.IsCancellationRequested || !ReferenceEquals(_preparing, cancellation))
                return;

            IsPreparing = false;
            if (image == null)
            {
                SizeText = Services.SizeText.Format(Size);
                return;
            }

            if (!image.CanCompress && CanCompress)
            {
                CanCompress = false;
                // Compressing was asked of an animation: what is left to do is done without it
                Prepare();
                return;
            }

            // An animation the server would refuse as larger than it takes can only go as a file
            if (!image.CanCompress && !_policy.AllowOriginals && image.LongerSide > _policy.MaxSide)
            {
                SendAsFile();
                return;
            }

            SizeText = image.Content is { } content && content.Length != Size
                ? $"{Services.SizeText.Format(Size)} → {Services.SizeText.Format(content.Length)}"
                : Services.SizeText.Format(Size);
        });
    }

    private void RaiseOptionsChanged()
    {
        this.RaisePropertyChanged(nameof(IsCompressionForced));
        this.RaisePropertyChanged(nameof(CanToggleCompression));
        this.RaisePropertyChanged(nameof(CanToggleMetadata));
        this.RaisePropertyChanged(nameof(IsCompressed));
    }

    private async Task LoadThumbnail(IChatImageService images) =>
        Thumbnail = await images.DecodeAsync(Source.Open, ThumbnailSide);
}
