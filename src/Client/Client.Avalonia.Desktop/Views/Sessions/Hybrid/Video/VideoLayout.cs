using System;
using Avalonia;

namespace Client.Views.Sessions.Hybrid.Video;

public enum StretchMode
{
    None,
    Fill,
    Uniform,
    UniformToFill
}

internal static class VideoLayout
{
    /// <summary>Where a picture of the given size goes inside <paramref name="bounds"/>.</summary>
    public static Rect Fit(Rect bounds, double width, double height, StretchMode stretch)
    {
        if (width <= 0 || height <= 0)
            return bounds;

        switch (stretch)
        {
            case StretchMode.Fill:
                return bounds;

            case StretchMode.Uniform:
            case StretchMode.UniformToFill:
            {
                var scale = stretch == StretchMode.Uniform
                    ? Math.Min(bounds.Width / width, bounds.Height / height)
                    : Math.Max(bounds.Width / width, bounds.Height / height);
                var w = width * scale;
                var h = height * scale;

                return new Rect(bounds.X + (bounds.Width - w) / 2, bounds.Y + (bounds.Height - h) / 2, w, h);
            }

            default:
                return new Rect(bounds.X, bounds.Y, width, height);
        }
    }
}
