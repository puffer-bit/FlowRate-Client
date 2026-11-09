using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Client.Core.Settings.Themes;
using Client.Services;
using Xunit;

namespace Client.Application.Tests;

[Collection("Application theme")]
public class ThemeServiceTests
{
    [Fact]
    public Task SystemColorsFollowActualThemeAndPreserveOverrides() => TestUiThread.RunAsync(() =>
    {
        var app = global::Avalonia.Application.Current!;
        var previous = app.RequestedThemeVariant;
        var resources = new ResourceDictionary();
        string[] keys = ["ColorPrimary", "ColorSecondary", "ColorDangerBase", "ColorSaveBase",
            "ColorSuccess", "ColorWarning", "ColorError"];
        foreach (var (variant, color) in new[] { (ThemeVariant.Dark, Colors.Red), (ThemeVariant.Light, Colors.Blue) })
        {
            var dictionary = new ResourceDictionary();
            foreach (var key in keys)
                dictionary[key] = color;
            resources.ThemeDictionaries[variant] = dictionary;
        }
        app.Resources.MergedDictionaries.Add(resources);
        try
        {
            var service = new ThemeService();
            foreach (var theme in new[] { ThemeType.Light, ThemeType.Dark, ThemeType.Light })
            {
                // Simulate changes of the effective theme without depending on the test host OS.
                service.ApplyTheme(theme);
                var expected = theme == ThemeType.Light ? Colors.Blue : Colors.Red;
                var colors = service.ResolveColors(new CustomColors { Primary = "#008000" }, ThemeType.System);
                Assert.Equal(Colors.Green, colors.Primary);
                Assert.Equal(expected, colors.Secondary);
            }

            service.ApplyTheme(ThemeType.System);
            Assert.Equal(ThemeVariant.Default, app.RequestedThemeVariant);
        }
        finally
        {
            app.Resources.MergedDictionaries.Remove(resources);
            app.RequestedThemeVariant = previous;
        }
    });
}

[CollectionDefinition("Application theme", DisableParallelization = true)]
public class ApplicationThemeCollection;
