using Client.Tools.Environment;
using Xunit;

namespace Client.Avalonia.Localization.Tests;

public class SystemLanguageTests
{
    [Theory]
    [InlineData("ru_RU.UTF-8", "ru")]
    [InlineData("ru-RU", "ru")]
    [InlineData("en_US", "en")]
    [InlineData("RU", "ru")]
    [InlineData("sr@latin", "sr")]
    [InlineData("C", null)]
    [InlineData("C.UTF-8", null)]
    [InlineData("POSIX", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void LanguageIsTakenOutOfTheLocale(string? locale, string? expected)
    {
        Assert.Equal(expected, SystemLanguage.ParseLocale(locale));
    }
}
