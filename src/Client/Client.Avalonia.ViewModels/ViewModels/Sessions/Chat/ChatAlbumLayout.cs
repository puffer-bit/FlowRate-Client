using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// Where the pictures of one message stand: an only one fitted into a box by its own shape, several put into rows
/// of one height each that fill the width of the album, as Telegram lays out an album.
/// </summary>
public static class ChatAlbumLayout
{
    // The box an only picture is fitted into
    public const double SingleMaxSide = 320;
    // The width of an album of several pictures, and the gap between them
    public const double AlbumWidth = 360;
    public const double Gap = 2;

    // A picture far wider or taller than this keeps its row readable: it is cut to this shape in its tile
    private const double MinAspect = 0.5;
    private const double MaxAspect = 2.5;
    // A row of one picture is not made taller than this share of the width
    private const double MaxSingleRowShare = 0.75;

    /// <summary>
    /// The tiles of pictures of the given sizes in pixels, in the same order; a size not known is 0.
    /// </summary>
    public static IReadOnlyList<Rect> Arrange(IReadOnlyList<(double Width, double Height)> sizes)
    {
        if (sizes.Count == 0)
            return [];
        if (sizes.Count == 1)
        {
            var (width, height) = FitSingle(sizes[0].Width, sizes[0].Height);
            return [new Rect(0, 0, width, height)];
        }

        var aspects = sizes
            .Select(size => size.Width > 0 && size.Height > 0 ? Math.Clamp(size.Width / size.Height, MinAspect, MaxAspect) : 1)
            .ToList();
        var tiles = new List<Rect>(sizes.Count);
        double y = 0;
        int start = 0;
        foreach (var count in RowCounts(sizes.Count))
        {
            var row = aspects.GetRange(start, count);
            double usable = AlbumWidth - Gap * (count - 1);
            double height = Math.Round(usable / row.Sum());
            if (count == 1)
                height = Math.Min(height, Math.Round(AlbumWidth * MaxSingleRowShare));

            double x = 0;
            for (int i = 0; i < count; i++)
            {
                // The last one takes what is left, so the row ends exactly at the edge
                double width = i == count - 1 ? AlbumWidth - x : Math.Round(row[i] * height);
                tiles.Add(new Rect(x, y, Math.Max(1, width), height));
                x += width + Gap;
            }
            y += height + Gap;
            start += count;
        }
        return tiles;
    }

    /// <summary>The size an only picture is shown at.</summary>
    public static (double Width, double Height) FitSingle(double width, double height)
    {
        if (width <= 0 || height <= 0)
            return (SingleMaxSide, SingleMaxSide);

        var scale = Math.Min(1, Math.Min(SingleMaxSide / width, SingleMaxSide / height));
        return (Math.Max(1, Math.Round(width * scale)), Math.Max(1, Math.Round(height * scale)));
    }

    /// <summary>The size of the whole album the tiles make.</summary>
    public static Size Extent(IEnumerable<Rect> tiles)
    {
        double width = 0, height = 0;
        foreach (var tile in tiles)
        {
            width = Math.Max(width, tile.Right);
            height = Math.Max(height, tile.Bottom);
        }
        return new Size(width, height);
    }

    // Three: one wide picture over two. Otherwise rows of three, where a remainder of one takes a picture from the
    // row before, so no row is left with a single picture
    private static IEnumerable<int> RowCounts(int total)
    {
        switch (total)
        {
            case 2: return [2];
            case 3: return [1, 2];
            case 4: return [2, 2];
        }

        var rows = new List<int>();
        int left = total;
        while (left > 0)
        {
            if (left == 4)
            {
                rows.Add(2);
                rows.Add(2);
                break;
            }
            int count = Math.Min(3, left);
            rows.Add(count);
            left -= count;
        }
        return rows;
    }
}
