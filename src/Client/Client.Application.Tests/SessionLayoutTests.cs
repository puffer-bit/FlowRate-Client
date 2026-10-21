using Client.Core.Sessions;
using Xunit;

namespace Client.Application.Tests;

public class SessionLayoutTests
{
    private static readonly SessionLayout Layout = new(5, [1, 2, 3],
        [new SessionCategory(10, "Voice", [4, 5]), new SessionCategory(20, "Games", [])]);

    [Fact]
    public void MoveSession_WithinRoot_TakesItOutFirst()
    {
        Assert.Equal([2, 3, 1], Layout.WithSessionMoved(1, null, 2).RootSessionIds);
    }

    [Fact]
    public void MoveSession_IntoCategory()
    {
        var moved = Layout.WithSessionMoved(2, 10, 1);

        Assert.Equal([1, 3], moved.RootSessionIds);
        Assert.Equal([4, 2, 5], moved.Categories[0].SessionIds);
        Assert.Equal(10, moved.CategoryOf(2));
        Assert.Equal(5, moved.Revision);
    }

    [Fact]
    public void MoveSession_PastTheEnd_GoesToTheEnd()
    {
        Assert.Equal([1], Layout.WithSessionMoved(1, 20, 99).Categories[1].SessionIds);
    }

    [Fact]
    public void MoveSession_ToUnknownCategory_ChangesNothing()
    {
        Assert.Same(Layout, Layout.WithSessionMoved(1, 99, 0));
    }

    [Fact]
    public void MoveSession_Unknown_IsInserted()
    {
        Assert.Equal([1, 9, 2, 3], Layout.WithSessionMoved(9, null, 1).RootSessionIds);
    }

    [Fact]
    public void MoveCategory_Reorders()
    {
        Assert.Equal([20, 10], Layout.WithCategoryMoved(10, 5).Categories.Select(category => category.Id));
    }

    [Fact]
    public void Original_IsNotChanged()
    {
        Layout.WithSessionMoved(1, 10, 0);

        Assert.Equal([1, 2, 3], Layout.RootSessionIds);
        Assert.Equal([4, 5], Layout.Categories[0].SessionIds);
    }
}
