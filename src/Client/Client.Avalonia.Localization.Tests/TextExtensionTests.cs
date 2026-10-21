using Avalonia;
using Avalonia.Controls;
using Xunit;

namespace Client.Avalonia.Localization.Tests;

[Collection(nameof(StringsTests))]
public class TextExtensionTests : IDisposable
{
    public void Dispose() => Strings.SetLanguage("en");

    [Fact]
    public void BoundPropertyFollowsTheLanguage()
    {
        var block = new TextBlock();
        block.Bind(TextBlock.TextProperty, new TextExtension("common.cancel").ProvideValue(null!));

        Assert.Equal("Cancel", block.Text);

        // Nothing but the control holds the binding: it must survive a collection
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Strings.SetLanguage("ru");

        Assert.Equal("Отмена", block.Text);
    }

    [Fact]
    public void ControlIsCollectedWithoutUnsubscribing()
    {
        var reference = BindAndForget();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(reference.TryGetTarget(out _));
    }

    private static WeakReference<TextBlock> BindAndForget()
    {
        var block = new TextBlock();
        block.Bind(TextBlock.TextProperty, new TextExtension("common.cancel").ProvideValue(null!));

        return new WeakReference<TextBlock>(block);
    }
}
