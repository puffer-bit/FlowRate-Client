using Avalonia.Controls;
using Client.Avalonia.Controls;
using Client.Avalonia.Controls.Settings;
using Xunit;

namespace Client.Application.Tests;

public class SettingsPageFoldingTests
{
    [Fact]
    public Task WrappedHeaderFoldsOnlyItsOwnSectionAndKeepsItsRowVisible() => TestUiThread.RunAsync(() =>
    {
        var page = new SettingsPage();
        var first = new SettingsHeader { SectionKey = "first" };
        var firstBody = new Border();
        var header = new SettingsHeader { SectionKey = "wrapped" };
        var row = new Grid();
        row.Children.Add(header);
        row.Children.Add(new TextBox());
        var body = new Border();
        var last = new SettingsHeader { SectionKey = "last" };
        var lastBody = new Border();
        foreach (var item in new Control[] { first, firstBody, row, body, last, lastBody })
            page.Items.Add(item);
        var collapsed = new List<string>();
        SettingsPage.SetCollapsedSections(page, collapsed);

        header.IsCollapsed = true;

        Assert.False(body.IsVisible);
        Assert.True(row.IsVisible);
        Assert.True(firstBody.IsVisible);
        Assert.True(lastBody.IsVisible);
        Assert.Contains("wrapped", collapsed);

        header.IsCollapsed = false;

        Assert.True(body.IsVisible);
        Assert.DoesNotContain("wrapped", collapsed);
    });

    [Fact]
    public Task RestoredWrappedSectionDoesNotHideFollowingHeaderOrOverrideVisibilityBinding() => TestUiThread.RunAsync(() =>
    {
        var page = new SettingsPage();
        var header = new SettingsHeader { SectionKey = "wrapped" };
        var row = new Grid();
        row.Children.Add(header);
        var body = new Border();
        var hiddenBody = new Border { IsVisible = false };
        var followingHeader = new SettingsHeader { SectionKey = "following" };
        var followingBody = new Border();
        foreach (var item in new Control[] { row, body, hiddenBody, followingHeader, followingBody })
            page.Items.Add(item);

        SettingsPage.SetCollapsedSections(page, new List<string> { "wrapped" });

        Assert.True(header.IsCollapsed);
        Assert.False(body.IsVisible);
        Assert.True(row.IsVisible);
        Assert.True(followingBody.IsVisible);

        header.IsCollapsed = false;

        Assert.True(body.IsVisible);
        Assert.False(hiddenBody.IsVisible);
    });
}
