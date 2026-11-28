using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Client.Avalonia.Controls.Images;
using Client.Avalonia.Localization;
using Client.ViewModels.Sessions.Chat;
using Client.Views.Common;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.Views.Sessions.Chat;

/// <summary>The same desktop viewer state for attachments, link pictures and avatars.</summary>
internal sealed class ImagePreviewVM : ReactiveObject, IDisposable
{
    private readonly ChatImageViewerVM? _attachment;
    private readonly IImage? _picture;
    private string? _error;
    private int _quarterTurns;
    private readonly bool _ownsCommands;

    public Control? Presentation { private get; set; }
    public string FileName { get; }
    public IImage? Picture => _attachment?.Picture ?? _picture;
    public bool IsLoading => _attachment?.IsLoading ?? false;
    public double Progress => _attachment?.Progress ?? 0;
    public string? Error => _attachment?.Error ?? _error;
    public bool HasError => Error != null;
    public int QuarterTurns => _attachment?.QuarterTurns ?? _quarterTurns;
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CopyCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CloseCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RotateCommand { get; }

    public ImagePreviewVM(ChatImageViewerVM attachment)
    {
        _attachment = attachment;
        FileName = attachment.FileName;
        SaveCommand = attachment.SaveCommand;
        CopyCommand = attachment.CopyCommand;
        CloseCommand = attachment.CloseCommand;
        RotateCommand = attachment.RotateCommand;
        attachment.PropertyChanged += AttachmentChanged;
    }

    public ImagePreviewVM(Control anchor, IImage picture, string title, Action close)
    {
        _ownsCommands = true;
        _picture = picture;
        FileName = title;
        var bitmap = picture as Bitmap ?? (picture as AnimatedImage)?.FirstFrame;
        SaveCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            try
            {
                if (bitmap == null || TopLevel.GetTopLevel(Presentation ?? anchor) is not { } owner) return;
                var target = await owner.StorageProvider.SaveFilePickerAsync(new()
                {
                    SuggestedFileName = "image.png", DefaultExtension = "png",
                    FileTypeChoices = [new("PNG") { Patterns = ["*.png"] }]
                });
                if (target == null) return;
                await using var stream = await target.OpenWriteAsync();
                bitmap.Save(stream, PngBitmapEncoderOptions.Default);
            }
            catch (Exception) { SetError("chat.image.saveFailed"); }
        });
        CopyCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            try
            {
                if (bitmap != null && !await ClipboardAccess.SetBitmapAsync(Presentation ?? anchor, bitmap))
                    SetError("chat.image.copyFailed");
            }
            catch (Exception) { SetError("chat.image.copyFailed"); }
        });
        CloseCommand = ReactiveCommand.Create(close);
        RotateCommand = ReactiveCommand.Create(() => { _quarterTurns = (_quarterTurns + 1) % 4; this.RaisePropertyChanged(nameof(QuarterTurns)); });
    }

    private void SetError(string key)
    {
        _error = Strings.Get(key);
        this.RaisePropertyChanged(nameof(Error));
        this.RaisePropertyChanged(nameof(HasError));
    }

    private void AttachmentChanged(object? sender, PropertyChangedEventArgs e) => this.RaisePropertyChanged(e.PropertyName);

    public void Dispose()
    {
        if (_attachment != null) _attachment.PropertyChanged -= AttachmentChanged;
        if (!_ownsCommands) return;
        SaveCommand.Dispose(); CopyCommand.Dispose(); CloseCommand.Dispose(); RotateCommand.Dispose();
    }
}
