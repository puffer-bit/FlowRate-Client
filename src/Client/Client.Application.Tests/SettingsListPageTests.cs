using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Client.ViewModels.Settings.Pages;
using Xunit;

namespace Client.Application.Tests;

public class SettingsListPageTests
{
    private static SettingsListPageVM<string> Page(ObservableCollection<string> source) =>
        new(source, (item, filter) => item.Contains(filter, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void SearchResetsPageAndFindsItemsOutsideTheCurrentPage()
    {
        var source = new ObservableCollection<string>(Enumerable.Range(1, 45).Select(i => $"Server {i}"));
        using var page = Page(source);
        page.NextPageCommand.Execute().Wait();
        Assert.Equal(2, page.CurrentPage);
        Assert.Equal(20, page.Items.Count);

        page.Filter = "  SERVER 45  ";

        Assert.Equal(1, page.CurrentPage);
        Assert.Equal(1, page.PageCount);
        Assert.Equal(["Server 45"], page.Items);
        Assert.False(page.HasPreviousPage);
        Assert.False(page.HasNextPage);
        Assert.Equal(45, page.TotalCount);
    }

    [Fact]
    public void RemovingLastItemOnLastPageReturnsToPreviousPage()
    {
        var source = new ObservableCollection<string>(Enumerable.Range(1, 21).Select(i => i.ToString()));
        using var page = Page(source);
        page.NextPageCommand.Execute().Wait();
        Assert.Equal(["21"], page.Items);

        source.Remove("21");

        Assert.Equal(1, page.CurrentPage);
        Assert.Equal(20, page.Items.Count);
        Assert.False(page.HasMultiplePages);
        Assert.False(page.HasNextPage);
    }

    [Fact]
    public void EmptySearchResultsRecoverWhenMatchingDataIsAdded()
    {
        var source = new ObservableCollection<string> { "Server" };
        using var page = Page(source);
        page.Filter = "certificate";
        Assert.Empty(page.Items);
        Assert.True(page.HasNoMatches);
        Assert.Equal(1, page.PageCount);

        source.Add("Certificate");

        Assert.Equal(["Certificate"], page.Items);
        Assert.False(page.HasNoMatches);
        source.Clear();
        Assert.Empty(page.Items);
        Assert.False(page.HasItems);
        Assert.False(page.HasNoMatches);
    }

    [Fact]
    public void ListsKeepIndependentFiltersAndPages()
    {
        var source = new ObservableCollection<string>(Enumerable.Range(1, 25).Select(i => $"Server {i}"));
        using var servers = Page(source);
        using var certificates = Page(new ObservableCollection<string> { "Certificate" });
        servers.NextPageCommand.Execute().Wait();

        certificates.Filter = "unknown";

        Assert.Equal(2, servers.CurrentPage);
        Assert.Equal(5, servers.Items.Count);
        Assert.True(certificates.HasNoMatches);
    }
}
