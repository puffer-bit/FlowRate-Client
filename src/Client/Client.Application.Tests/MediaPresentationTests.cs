using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Client.Avalonia.Controls.Images;
using Xunit;

namespace Client.Application.Tests;

public sealed class MediaPresentationTests
{
    [Theory]
    [InlineData(400, 225, 200, 200, 112.5)]
    [InlineData(225, 400, 112.5, 112.5, 200)]
    [InlineData(100, 50, 600, 100, 50)]
    public Task MediaFrame_PreservesProportionsInNarrowColumns(double width, double height,
        double availableWidth, double expectedWidth, double expectedHeight) => TestUiThread.RunAsync(() =>
    {
        var frame = new MediaFrame { MediaWidth = width, MediaHeight = height, Child = new Border(), UseLayoutRounding = false };
        frame.Measure(new Size(availableWidth, double.PositiveInfinity));
        Assert.Equal(expectedWidth, frame.DesiredSize.Width, 3);
        Assert.Equal(expectedHeight, frame.DesiredSize.Height, 3);
    });

    [Fact]
    public Task RotatingAnImage_RefitsAndUpdatesItsHitArea() => TestUiThread.RunAsync(() =>
    {
        var image = new ZoomableImage { Source = new TestImage(new Size(400, 100)) };
        image.Measure(new Size(400, 300));
        image.Arrange(new Rect(0, 0, 400, 300));
        Assert.True(image.HitTest(new Point(10, 150)));
        Assert.False(image.HitTest(new Point(200, 10)));

        image.QuarterTurns = 1;
        Assert.False(image.HitTest(new Point(10, 150)));
        Assert.True(image.HitTest(new Point(200, 10)));

        image.QuarterTurns = 4;
        Assert.True(image.HitTest(new Point(10, 150)));
        Assert.False(image.HitTest(new Point(200, 10)));
    });

    private sealed class TestImage(Size size) : IImage
    {
        public Size Size { get; } = size;
        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
        {
            context.DrawRectangle(Brushes.Red, null, new Rect(destRect.X, destRect.Y, destRect.Width / 2, destRect.Height));
            context.DrawRectangle(Brushes.Blue, null, new Rect(destRect.X + destRect.Width / 2, destRect.Y, destRect.Width / 2, destRect.Height));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public Task RotatingAnImage_DrawsTheWholePictureInsideItsNewBounds(int sourceKind) => TestUiThread.RunAsync(() =>
    {
        var image = new ZoomableImage { Source = new TestImage(new Size(400, 100)), QuarterTurns = 1 };
        using var source = new RenderTargetBitmap(new PixelSize(400, 100), new Vector(96, 96));
        if (sourceKind == 1)
        {
            var original = new ZoomableImage { Source = image.Source };
            original.Measure(new Size(400, 100));
            original.Arrange(new Rect(0, 0, 400, 100));
            source.Render(original);
            image.Source = source;
        }
        else if (sourceKind == 2)
        {
            var drawing = new DrawingGroup();
            drawing.Children.Add(new GeometryDrawing { Brush = Brushes.Red, Geometry = new RectangleGeometry(new Rect(0, 0, 200, 100)) });
            drawing.Children.Add(new GeometryDrawing { Brush = Brushes.Blue, Geometry = new RectangleGeometry(new Rect(200, 0, 200, 100)) });
            image.Source = new DrawingImage(drawing);
        }
        var host = new Grid { ColumnDefinitions = new ColumnDefinitions("100,400"), RowDefinitions = new RowDefinitions("50,300") };
        Grid.SetColumn(image, 1);
        Grid.SetRow(image, 1);
        host.Children.Add(image);
        host.Measure(new Size(500, 350));
        host.Arrange(new Rect(0, 0, 500, 350));
        using var target = new RenderTargetBitmap(new PixelSize(500, 350), new Vector(96, 96));
        target.Render(host);
        using var stream = new MemoryStream();
        target.Save(stream, PngBitmapEncoderOptions.Default);
        using var pixels = SkiaSharp.SKBitmap.Decode(stream.ToArray());
        Assert.Equal(SkiaSharp.SKColors.Red, pixels.GetPixel(300, 60));
        Assert.Equal(SkiaSharp.SKColors.Blue, pixels.GetPixel(300, 340));
        Assert.Equal(0, pixels.GetPixel(110, 200).Alpha);
    });
}
