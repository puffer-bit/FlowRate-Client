using System.Reactive.Linq;
using Avalonia;
using Avalonia.Skia;
using Client.Services;
using Client.ViewModels.Profile;
using Client.ViewModels.Sessions.Chat;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using Xunit;

namespace Client.Application.Tests;

public class AvatarEditingTests
{
    static AvatarEditingTests() => SkiaPlatform.Initialize();

    private static AvatarService Service() => new(null!, new DecodeLogger());

    private sealed class DecodeLogger : ILogger<AvatarService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => throw new InvalidOperationException("Avatar decoding failed", exception);
    }

    private static PickedFile Photo()
    {
        using var bitmap = new SKBitmap(400, 200);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Red);
            using var blue = new SKPaint { Color = SKColors.Blue };
            canvas.DrawRect(new SKRect(200, 0, 400, 200), blue);
        }
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = data.ToArray();
        return new PickedFile("photo.png", null, bytes.Length,
            () => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false)));
    }

    private static void AssertColor(PreparedAvatar avatar, bool blue)
    {
        using var bitmap = SKBitmap.Decode(avatar.Encoded);
        Assert.Equal("image/webp", avatar.MimeType);
        Assert.Equal(256, bitmap.Width);
        Assert.Equal(256, bitmap.Height);
        var pixel = bitmap.GetPixel(128, 128);
        Assert.True(blue ? pixel.Blue > 240 && pixel.Red < 15 : pixel.Red > 240 && pixel.Blue < 15);
    }

    [Fact]
    public async Task SelectedAreaIsEncodedInsteadOfTheMiddle()
    {
        using var editor = await Service().OpenEditorAsync(Photo().Open);
        Assert.NotNull(editor);
        var avatar = editor.Prepare(new Rect(0.5, 0, 0.5, 1));
        using var picture = (IDisposable)avatar.Picture;
        AssertColor(avatar, blue: true);
    }

    [Fact]
    public async Task RotationAndMirrorChangeTheUploadedPixels()
    {
        using var editor = await Service().OpenEditorAsync(Photo().Open);
        Assert.NotNull(editor);
        editor.Transform(mirror: true);
        var mirrored = editor.Prepare(new Rect(0, 0, 0.5, 1));
        using var mirrorPicture = (IDisposable)mirrored.Picture;
        AssertColor(mirrored, blue: true);

        editor.Transform(mirror: false);
        Assert.Equal(new Size(200, 400), editor.Picture.Size);
        var rotated = editor.Prepare(new Rect(0, 0.5, 1, 0.5));
        using var rotatePicture = (IDisposable)rotated.Picture;
        AssertColor(rotated, blue: false);
    }

    [Fact]
    public async Task ResetRestoresOriginalPixelsAndDefaultCropAfterRepeatedEdits()
    {
        using var editor = await Service().OpenEditorAsync(Photo().Open);
        Assert.NotNull(editor);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            editor.Transform(mirror: true);
            editor.Transform(mirror: false);
            var crop = new global::Client.Avalonia.Controls.Avatars.AvatarCropCanvas();
            crop.SetPicture(editor.Picture);
            crop.Zoom = 3;

            editor.Reset();
            crop.SetPicture(editor.Picture);

            Assert.Equal(new Size(400, 200), editor.Picture.Size);
            Assert.Equal(1, crop.Zoom);
            Assert.Equal(new Rect(0.25, 0, 0.5, 1), crop.Crop);
            var left = editor.Prepare(new Rect(0, 0, 0.5, 1));
            using var leftPicture = (IDisposable)left.Picture;
            AssertColor(left, blue: false);
            var right = editor.Prepare(new Rect(0.5, 0, 0.5, 1));
            using var rightPicture = (IDisposable)right.Picture;
            AssertColor(right, blue: true);
        }
    }

    [Fact]
    public async Task CancellingEditorKeepsThePreviousSelection()
    {
        using var picker = new AvatarPickerVM(Service());
        using var fileHandler = picker.PickAvatarFile.RegisterHandler(context => context.SetOutput(Photo()));
        var cancelled = false;
        using var editHandler = picker.EditAvatar.RegisterHandler(context => context.SetOutput(cancelled
            ? null
            : context.Input.Prepare(new Rect(0, 0, 0.5, 1))));
        await picker.ChooseAvatarCommand.Execute();
        var previous = picker.Picked;
        Assert.NotNull(previous);

        cancelled = true;
        await picker.ChooseAvatarCommand.Execute();

        Assert.Same(previous, picker.Picked);
        Assert.Same(previous.Picture, picker.Avatar);
        Assert.Null(picker.AvatarError);
    }
}
