using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Client.Services.Tray;

/// <summary>
/// What the tray icon shows of us while we are in a hybrid session.
/// </summary>
internal enum TrayVoiceState
{
    Idle,
    Connecting,
    Speaking,
    MicrophoneOff,
    SoundOff,
    Afk,
}

/// <summary>
/// Draws the tray icon of a voice state: a plain circle, green while we speak, or alone the glyph the
/// session window shows for a microphone or a sound that is off, or for being away. Drawn big and with
/// thick strokes, so that it still reads once the tray shrinks it. Every state is drawn once and kept.
/// </summary>
internal static class TrayIconRenderer
{
    private const int Size = 64;

    // The glyphs are the ones of Client.Avalonia.Assets/IconImages.axaml, on their 24x24 grid
    private static readonly string[] Microphone =
    [
        "M12,3 H12 A3,3 0 0 1 15,6 V11 A3,3 0 0 1 12,14 H12 A3,3 0 0 1 9,11 V6 A3,3 0 0 1 12,3 Z",
        "M5 11 A7 7 0 0 0 19 11",
        "M12 18 V21",
        "M8 21 H16",
    ];

    private static readonly string[] Headphones =
    [
        "M4 14 V12 A8 8 0 0 1 20 12 V14",
        "M4.5,14 H5.5 A1.5,1.5 0 0 1 7,15.5 V18.5 A1.5,1.5 0 0 1 5.5,20 H4.5 A1.5,1.5 0 0 1 3,18.5 V15.5 A1.5,1.5 0 0 1 4.5,14 Z",
        "M18.5,14 H19.5 A1.5,1.5 0 0 1 21,15.5 V18.5 A1.5,1.5 0 0 1 19.5,20 H18.5 A1.5,1.5 0 0 1 17,18.5 V15.5 A1.5,1.5 0 0 1 18.5,14 Z",
    ];

    private static readonly string[] Cup =
    [
        "M4,10 H16 V15 A5,5 0 0 1 11,20 H9 A5,5 0 0 1 4,15 Z",
        "M16,11.5 H17.5 A2.5,2.5 0 0 1 17.5,16.5 H15.5",
        "M8,2.5 C7,3.75 9,4.75 8,6",
        "M12,2.5 C11,3.75 13,4.75 12,6",
    ];

    private const string Cross = "M4 4 L20 20";

    private static readonly Color Gray = Color.Parse("#8E9297");
    private static readonly Color Green = Color.Parse("#23A55A");
    private static readonly Color Glyph = Color.Parse("#F2F3F5");

    private static readonly Dictionary<TrayVoiceState, WindowIcon> Cache = [];

    /// <summary>Must be called on the UI thread.</summary>
    public static WindowIcon Get(TrayVoiceState state)
    {
        if (!Cache.TryGetValue(state, out var icon))
            Cache[state] = icon = new WindowIcon(Render(state));
        return icon;
    }

    private static Bitmap Render(TrayVoiceState state)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(Size, Size), new Vector(96, 96));
        using var context = bitmap.CreateDrawingContext();

        var glyph = state switch
        {
            TrayVoiceState.MicrophoneOff => Microphone,
            TrayVoiceState.SoundOff => Headphones,
            TrayVoiceState.Afk => Cup,
            _ => null,
        };

        if (glyph == null)
        {
            var color = state == TrayVoiceState.Speaking ? Green : Gray;
            var opacity = state == TrayVoiceState.Connecting ? 0.5 : 1.0;
            context.DrawEllipse(new SolidColorBrush(color, opacity), null, new Point(Size / 2.0, Size / 2.0), 24, 24);
            return bitmap;
        }

        // The 24x24 grid of the glyph fills the icon
        const double scale = Size / 24.0;
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            var pen = new Pen(new SolidColorBrush(Glyph), 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            foreach (var path in glyph)
                context.DrawGeometry(null, pen, Geometry.Parse(path));

            if (state != TrayVoiceState.Afk)
                context.DrawGeometry(null, pen, Geometry.Parse(Cross));
        }

        return bitmap;
    }
}
