using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using Client.Application.Chat;
using Client.Application.Ports;
using Client.Application.Tasks;
using Client.Avalonia.Localization;
using Client.Core.Chat;
using Client.Services;
using ReactiveUI;

using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// A file attached to the message being typed, shown as a card above the input. An image goes as one and
/// is shown in the message; any other file goes as it is, to be downloaded. An image or a video may be
/// compressed and stripped of its metadata before it goes, each chosen on its card.
/// </summary>
public class ChatAttachmentVM : ReactiveObject, IDisposable
{
    private static readonly HashSet<string> VideoExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".3gp" };

    // The card is small; the picture is decoded no larger than it is drawn at double density
    private const int ThumbnailSide = 128;

    private readonly IImagePreparer? _preparer;
    private ImageCompressionPolicy _policy;
    private readonly IVideoPreparer? _videoPreparer;
    private VideoCompressionPolicy _videoPolicy;

    // The video as it will go, for the current choices; a newer one replaces it and deletes its file
    private Task<PreparedVideo?>? _preparedVideo;
    private bool _isDisposed;

    // The preparation for the current choices; a newer one replaces it
    private CancellationTokenSource? _preparing;
    private Task<PreparedImage?>? _prepared;

    // The compression of the video in the task list, while it runs
    private readonly ITaskTracker? _tasks;
    private readonly string? _taskContext;
    private ITrackedTask? _compression;

    // The choices are set by the constructor before the first preparation
    private bool _isConstructed;

    public string FileName { get; }
    public string? Key { get; }
    public long Size { get; }

    /// <summary>The size of the file, and what it is down to once compressed or stripped.</summary>
    public string SizeText
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(OutgoingStatusText));
        }
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

    /// <summary>
    /// A video by its name, which this client can compress and strip: it still goes as a file.
    /// </summary>
    public bool IsVideo { get; }

    /// <summary>An image or a video: what can be compressed and stripped.</summary>
    public bool HasMediaOptions => IsImage || IsVideo;

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

    /// <summary>The server takes only compressed images or videos, so this one cannot go as it is.</summary>
    public bool IsCompressionForced => (IsImage && CanCompress && !_policy.AllowOriginals) ||
                                       (IsVideo && !_videoPolicy.AllowOriginals);

    public bool IsImageCompressionForced => IsImage && IsCompressionForced;
    public bool IsVideoCompressionForced => IsVideo && IsCompressionForced;

    public bool CanToggleCompression => ((IsImage && CanCompress) || IsVideo) && !IsCompressionForced && !IsLocked;
    public bool CanToggleMetadata => HasMediaOptions && !IsLocked;

    /// <summary>The resolution of a video can be picked: it is compressed.</summary>
    public bool CanChooseVideoSide => IsVideo && IsCompressed && !IsLocked;

    /// <summary>The shorter side a compressed video goes with, like 720 for 720p.</summary>
    public int VideoSide
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            foreach (var option in VideoSides)
                option.IsSelected = option.Side == value;
        }
    }

    /// <summary>The resolutions a video may be compressed to, those the server takes.</summary>
    public IReadOnlyList<ChatVideoSideOptionVM> VideoSides
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = [];

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
    public ReactiveCommand<int, RxVoid> ChooseVideoSideCommand { get; }

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
            this.RaisePropertyChanged(nameof(CanChooseVideoSide));
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
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(OutgoingStatusText));
        }
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
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(OutgoingStatusText));
        }
    }

    /// <summary>The share of the file the server holds, from 0 to 1.</summary>
    public double Progress
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(OutgoingStatusText));
        }
    }

    /// <summary>
    /// What the file shows in a message on its way: compressing, the share uploaded, its size once uploaded, or that
    /// it waits for its turn.
    /// </summary>
    public string OutgoingStatusText => IsPreparing
        ? Strings.Get("chat.outgoing.compressing")
        : IsUploading
            ? $"{Math.Round(Progress * 100):0}%"
            : Progress >= 1
                ? SizeText
                : Strings.Get("chat.outgoing.inQueue");

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
    /// <param name="videoPreparer">Null to send videos as they are.</param>
    /// <param name="tasks">Shows the compression of a video in the task list.</param>
    /// <param name="taskContext">The chat the task list names.</param>
    public ChatAttachmentVM(PickedFile file, bool isImage, IChatImageService images, IImagePreparer? preparer = null,
        ChatAttachmentPreferences? preferences = null, ImageCompressionPolicy? policy = null,
        IVideoPreparer? videoPreparer = null, VideoCompressionPolicy? videoPolicy = null,
        ITaskTracker? tasks = null, string? taskContext = null)
    {
        _tasks = tasks;
        _taskContext = taskContext;
        FileName = file.Name;
        Key = file.Key;
        Size = file.Size;
        SizeText = Services.SizeText.Format(file.Size);
        // A path of this disk is read in place by what cannot take a stream, as the video compressor
        Source = new ChatMediaSource(file.Name, file.Size, file.Open,
            file.Key != null && Path.IsPathFullyQualified(file.Key) && File.Exists(file.Key) ? file.Key : null);
        _preparer = preparer;
        _policy = policy ?? ImageCompressionPolicy.Default;
        _videoPolicy = videoPolicy ?? VideoCompressionPolicy.Default;
        IsVideo = !isImage && videoPreparer?.IsAvailable == true && VideoExtensions.Contains(Path.GetExtension(file.Name));
        _videoPreparer = IsVideo ? videoPreparer : null;
        UpdateVideoSides(preferences?.VideoSide);
        IsImage = isImage;
        // Known for sure once the file is read; a GIF is most likely animated
        CanCompress = isImage && preparer != null && !file.Name.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);

        if (preparer != null && preferences != null && isImage)
        {
            IsCompressed = preferences.CompressImages;
            IsMetadataStripped = preferences.StripMetadata;
        }
        else if (IsVideo)
        {
            preferences ??= new ChatAttachmentPreferences();
            IsCompressed = preferences.CompressVideos;
            IsMetadataStripped = preferences.StripMetadata;
        }

        ToggleCompressionCommand = ReactiveCommand.Create(() => { IsCompressed = !IsCompressed; },
            this.WatchProperties(nameof(CanToggleCompression)).Select(vm => vm.CanToggleCompression));
        ToggleMetadataCommand = ReactiveCommand.Create(() => { IsMetadataStripped = !IsMetadataStripped; },
            this.WatchProperties(nameof(CanToggleMetadata)).Select(vm => vm.CanToggleMetadata));
        ChooseVideoSideCommand = ReactiveCommand.Create<int>(side =>
            {
                if (side == VideoSide || !CanChooseVideoSide)
                    return;
                VideoSide = side;
                Prepare();
            },
            this.WatchProperties(nameof(CanChooseVideoSide)).Select(vm => vm.CanChooseVideoSide));

        _isConstructed = true;
        if (isImage)
            _ = LoadThumbnail(images);
        if (isImage || IsVideo)
            Prepare();
    }

    /// <summary>The server asks videos to be compressed otherwise.</summary>
    public void ApplyVideoPolicy(VideoCompressionPolicy policy)
    {
        if (policy == _videoPolicy || !IsVideo)
            return;
        _videoPolicy = policy;
        var side = VideoSide;
        UpdateVideoSides(side);
        RaiseOptionsChanged();
        Prepare();
    }

    // The side stays where the server still takes it
    private void UpdateVideoSides(int? chosen)
    {
        VideoSides = _videoPolicy.Sides.Select(side => new ChatVideoSideOptionVM(side)).ToList();
        VideoSide = _videoPolicy.SideFor(chosen);
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
        if (IsVideo)
            return await GetVideoUploadSourceAsync(cancellationToken);

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

    private async Task<ChatMediaSource> GetVideoUploadSourceAsync(CancellationToken cancellationToken)
    {
        if (_preparedVideo is not { } prepared)
            return Source;

        PreparedVideo? video;
        try { video = await prepared.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return Source; }
        // Could not be written again: it goes as it is, and a server that takes only compressed videos says so
        catch (InvalidOperationException) { return Source; }

        if (video?.Path is not { } path)
            return Source;
        return new ChatMediaSource(video.FileName, video.Size,
            () => Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
                useAsync: true)), path);
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
        if (IsVideo)
        {
            PrepareVideo();
            return;
        }

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

        // On the same line as the size, so the card keeps its height and the input below does not move
        IsPreparing = true;
        SizeText = $"{Services.SizeText.Format(Size)} → …";
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

    private void PrepareVideo()
    {
        if (_videoPreparer == null || !_isConstructed || _isDisposed)
            return;

        _preparing?.Cancel();
        var cancellation = _preparing = new CancellationTokenSource();
        ReleasePreparedVideo();
        ResetUpload();

        // Even with nothing chosen an MP4 with its index at the end is put together again, so it plays
        // while it downloads; anything else is checked and goes as it is
        var preparation = new VideoPreparation(IsCompressed, IsMetadataStripped, VideoCompressionPreset.For(VideoSide));
        IsPreparing = true;
        SizeText = $"{Services.SizeText.Format(Size)} → …";
        // The compression of the earlier choices is over
        _compression?.Dispose();
        var task = _compression = _tasks?.Begin(TrackedTaskKind.Compression, TrackedTaskStage.Compressing, FileName,
            _taskContext, totalBytes: Size);
        var progress = new Progress<double>(share =>
        {
            if (!ReferenceEquals(_preparing, cancellation) || !IsPreparing)
                return;
            task?.Report(share);
            if (share < 1)
                SizeText = $"{Services.SizeText.Format(Size)} → {share:P0}";
        });
        var prepared = _preparedVideo = Task.Run(() => _videoPreparer.PrepareAsync(Source, preparation, progress,
            cancellation.Token));
        _ = ShowPreparedVideoAsync(prepared, cancellation, task);
    }

    private async Task ShowPreparedVideoAsync(Task<PreparedVideo?> prepared, CancellationTokenSource cancellation,
        ITrackedTask? task)
    {
        PreparedVideo? video;
        var isFailed = false;
        try { video = await prepared; }
        catch (OperationCanceledException)
        {
            task?.Dispose();
            return;
        }
        catch (Exception)
        {
            video = null;
            isFailed = true;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (cancellation.IsCancellationRequested || !ReferenceEquals(_preparing, cancellation))
            {
                task?.Dispose();
                return;
            }

            // It still goes, as it was picked
            if (isFailed)
                task?.Fail(Strings.Get("tasks.compressFailed"));
            else
                task?.Complete();
            IsPreparing = false;
            SizeText = video?.Path != null && video.Size != Size
                ? $"{Services.SizeText.Format(Size)} → {Services.SizeText.Format(video.Size)}"
                : Services.SizeText.Format(Size);
        });
    }

    // The file of a preparation that is replaced or no longer needed, once it is done
    private void ReleasePreparedVideo()
    {
        if (Interlocked.Exchange(ref _preparedVideo, null) is not { } previous)
            return;
        _ = previous.ContinueWith(task =>
        {
            if (task.IsCompletedSuccessfully)
                task.Result?.Dispose();
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// The card is gone: a compression still running stops and the compressed file is deleted.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _preparing?.Cancel();
        _compression?.Dispose();
        ReleasePreparedVideo();
    }

    private void RaiseOptionsChanged()
    {
        this.RaisePropertyChanged(nameof(IsCompressionForced));
        this.RaisePropertyChanged(nameof(IsImageCompressionForced));
        this.RaisePropertyChanged(nameof(IsVideoCompressionForced));
        this.RaisePropertyChanged(nameof(CanToggleCompression));
        this.RaisePropertyChanged(nameof(CanToggleMetadata));
        this.RaisePropertyChanged(nameof(IsCompressed));
        this.RaisePropertyChanged(nameof(CanChooseVideoSide));
    }

    private async Task LoadThumbnail(IChatImageService images) =>
        Thumbnail = await images.DecodeAsync(Source.Open, ThumbnailSide);
}
