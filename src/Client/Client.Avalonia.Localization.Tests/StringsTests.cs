using Xunit;

namespace Client.Avalonia.Localization.Tests;

// Strings is static, so the tests that switch the language must not run next to each other
[Collection(nameof(StringsTests))]
public class StringsTests : IDisposable
{
    public void Dispose() => Strings.SetLanguage("en");

    [Fact]
    public void UnknownLanguageFallsBackToEnglish()
    {
        Strings.SetLanguage("ru");
        Strings.SetLanguage("xx");

        Assert.Same(Strings.Invariant, Strings.Current);
    }

    [Theory]
    [InlineData("ru-RU")]
    [InlineData("ru_UA")]
    [InlineData("RU")]
    public void RegionWithoutATranslationUsesTheLanguage(string code)
    {
        Strings.SetLanguage(code);

        Assert.Equal("ru", Strings.Current.Code);
    }

    [Fact]
    public void UnknownKeyIsReturnedAsIs()
    {
        Assert.Equal("no.such.key", Strings.Get("no.such.key"));
        Assert.Equal("no.such.key", Strings.Format("no.such.key", ("name", 1)));
        Assert.Null(Strings.Find("no.such.key"));
    }

    [Fact]
    public void GetFollowsTheLanguage()
    {
        Strings.SetLanguage("ru");

        Assert.Equal("Отмена", Strings.Get("common.cancel"));
        Assert.Equal("Cancel", Strings.FindInvariant("common.cancel"));
    }

    [Fact]
    public void FormatPutsTheArgumentsByName()
    {
        Assert.Equal("Connecting to example.com:26666 as Anna...",
            Strings.Format("main.status.connecting", ("user", "Anna"), ("host", "example.com"), ("port", 26666)));
    }

    [Fact]
    public void MessageWithoutItsArgumentIsShownAsWritten()
    {
        Assert.Equal("Reply to {name}", Strings.Get("chat.replyTo"));
    }

    [Fact]
    public void ApostropheIsKept()
    {
        Assert.Contains("Let's Encrypt", Strings.Get("settings.accounting.certificatesHint"));
    }

    [Theory]
    [InlineData("en", 1, "Its saved account and password")]
    [InlineData("en", 3, "Its 3 saved accounts")]
    [InlineData("ru", 1, "Сохранённая учётная запись")]
    [InlineData("ru", 3, "3 сохранённые учётные записи")]
    [InlineData("ru", 5, "5 сохранённых учётных записей")]
    [InlineData("ru", 21, "Сохранённая учётная запись")]
    public void FormatPicksThePluralFormOfTheLanguage(string language, int count, string start)
    {
        Strings.SetLanguage(language);

        Assert.StartsWith(start, Strings.Format("settings.accounting.forgetServer.accounts", ("count", count)));
    }

    [Fact]
    public void ObservedTextIsPublishedAgainOnLanguageChange()
    {
        var texts = new List<string>();
        using var subscription = Strings.Observe("common.cancel").Subscribe(new Observer(texts));

        Strings.SetLanguage("ru");
        Strings.SetLanguage("en");

        Assert.Equal(["Cancel", "Отмена", "Cancel"], texts);
    }

    [Fact]
    public void DisposedSubscriptionGetsNothing()
    {
        var texts = new List<string>();
        Strings.Observe("common.cancel").Subscribe(new Observer(texts)).Dispose();

        Strings.SetLanguage("ru");

        Assert.Equal(["Cancel"], texts);
    }

    [Fact]
    public void LanguageChangedIsNotRaisedForTheSameLanguage()
    {
        var raised = 0;
        void OnChanged() => raised++;
        Strings.LanguageChanged += OnChanged;

        try
        {
            Strings.SetLanguage("en");
            Strings.SetLanguage("ru");
            Strings.SetLanguage("ru");
        }
        finally
        {
            Strings.LanguageChanged -= OnChanged;
        }

        Assert.Equal(1, raised);
    }

    [Fact]
    public void LongDateTakesTheMonthFromTheTable()
    {
        var date = new DateTime(2026, 10, 5);

        Assert.Equal("5 October 2026", Strings.FormatLongDate(date));

        Strings.SetLanguage("ru");

        Assert.Equal("5 октября 2026", Strings.FormatLongDate(date));
        Assert.Equal("05.10.2026", Strings.FormatDate(date));
    }

    private sealed class Observer(List<string> texts) : IObserver<string>
    {
        public void OnNext(string value) => texts.Add(value);
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }
}
