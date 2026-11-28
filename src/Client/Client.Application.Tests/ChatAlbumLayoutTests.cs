using Client.ViewModels.Sessions.Chat;
using Xunit;

namespace Client.Application.Tests;

public sealed class ChatAlbumLayoutTests
{
    [Fact]
    public void An_only_picture_keeps_its_shape_within_the_box()
    {
        var tiles = ChatAlbumLayout.Arrange([(1600, 900)]);

        var tile = Assert.Single(tiles);
        Assert.Equal(ChatAlbumLayout.SingleMaxSide, tile.Width);
        Assert.Equal(180, tile.Height);
    }

    [Fact]
    public void A_small_only_picture_is_not_enlarged()
    {
        var tile = Assert.Single(ChatAlbumLayout.Arrange([(100, 50)]));

        Assert.Equal(100, tile.Width);
        Assert.Equal(50, tile.Height);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 2)]
    [InlineData(7, 3)]
    [InlineData(10, 4)]
    public void Rows_fill_the_album_width_and_none_is_left_with_one_picture(int count, int rows)
    {
        var sizes = Enumerable.Range(0, count).Select(i => (i % 2 == 0 ? 1200.0 : 800.0, 900.0)).ToList();

        var tiles = ChatAlbumLayout.Arrange(sizes);

        Assert.Equal(count, tiles.Count);
        var byRow = tiles.GroupBy(tile => tile.Y).ToList();
        Assert.Equal(rows, byRow.Count);
        foreach (var row in byRow)
        {
            Assert.Equal(ChatAlbumLayout.AlbumWidth, row.Max(tile => tile.Right));
            Assert.Single(row.Select(tile => tile.Height).Distinct());
            // A row of one is only the wide first picture of three
            if (row.Count() == 1)
                Assert.Equal(3, count);
        }
    }

    [Fact]
    public void Tiles_do_not_overlap()
    {
        var tiles = ChatAlbumLayout.Arrange([(1000, 1000), (500, 1000), (2000, 1000), (1000, 700), (900, 1600)]);

        for (int i = 0; i < tiles.Count; i++)
            for (int j = i + 1; j < tiles.Count; j++)
                Assert.False(tiles[i].Intersects(tiles[j]), $"tiles {i} and {j} overlap");
    }

    [Fact]
    public void Pictures_of_unknown_size_are_square()
    {
        var tiles = ChatAlbumLayout.Arrange([(0, 0), (0, 0)]);

        Assert.All(tiles, tile => Assert.Equal(tile.Height, tile.Width, 0));
    }
}
