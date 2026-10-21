using System.Globalization;
using System.Reflection;

namespace Client.Avalonia.Localization;

/// <summary>
/// Where the interface takes its texts from. The languages are the <c>Languages/*.arb</c> files built
/// into this assembly. The language can change while the application runs: markup follows it through
/// <see cref="TextExtension"/>, code through <see cref="Observe"/> or <see cref="LanguageChanged"/>.
/// A text read once with <see cref="Get"/> stays in the language it was read in.
/// </summary>
public static class Strings
{
    private const string InvariantCode = "en";
    private const string LanguageFileExtension = ".arb";
    private const int MinObservablesBeforeCleanup = 256;

    private static readonly List<WeakReference<TextObservable>> Observables = [];
    private static readonly IReadOnlyDictionary<string, object?> NoArguments = new Dictionary<string, object?>();
    private static int _cleanupAt = MinObservablesBeforeCleanup;

    // The tables a text is looked up in, in order: the language, the language without its region, English
    private static StringTable[] _lookup;

    static Strings()
    {
        Languages = LoadLanguages();
        Invariant = Languages.First(language => language.Code == InvariantCode);
        Current = Invariant;
        _lookup = [Invariant];
    }

    /// <summary>English: the fallback for keys a translation lacks, and what search matches besides the current language</summary>
    public static StringTable Invariant { get; }

    /// <summary>English first, the others by their names.</summary>
    public static IReadOnlyList<StringTable> Languages { get; }

    public static StringTable Current { get; private set; }

    /// <summary>
    /// Raised on the thread that changed the language, after every text of <see cref="Observe"/> was
    /// updated. Subscribers that live shorter than the application have to unsubscribe.
    /// </summary>
    public static event Action? LanguageChanged;

    /// <param name="code">
    /// Language code such as "ru" or "pt-BR". Without a translation for the region the one of the
    /// language is used, and English when there is none at all.
    /// </param>
    public static void SetLanguage(string? code)
    {
        var language = FindLanguage(code) ?? FindLanguage(BaseCode(code)) ?? Invariant;
        if (language == Current)
            return;

        Current = language;
        _lookup = new[] { language, FindLanguage(BaseCode(language.Code)), Invariant }
            .OfType<StringTable>()
            .Distinct()
            .ToArray();

        List<TextObservable> alive = [];
        lock (Observables)
        {
            Observables.RemoveAll(reference => !reference.TryGetTarget(out _));
            foreach (var reference in Observables)
            {
                if (reference.TryGetTarget(out var observable))
                    alive.Add(observable);
            }
        }

        foreach (var observable in alive)
            observable.Publish();

        LanguageChanged?.Invoke();
    }

    /// <returns>The text in the current language, in English when it is not translated, or the key itself when it is unknown.</returns>
    public static string Get(string key) => Find(key) ?? key;

    public static string? Find(string key) => Format(_lookup, key, NoArguments);

    public static string? FindInvariant(string key) => Format([Invariant], key, NoArguments);

    /// <summary>
    /// The text of <paramref name="key"/> with its <c>{name}</c> placeholders replaced by the arguments
    /// of the same names. A number picks the plural form in <c>{name, plural, ...}</c>.
    /// </summary>
    public static string Format(string key, params (string Name, object? Value)[] arguments)
    {
        var values = new Dictionary<string, object?>(arguments.Length);
        foreach (var (name, value) in arguments)
            values[name] = value;

        return Format(_lookup, key, values) ?? key;
    }

    /// <summary>A date in digits, in the order the language writes it.</summary>
    public static string FormatDate(DateTime date) => date.ToString(Get("date.short"), CultureInfo.InvariantCulture);

    public static string FormatDateTime(DateTime date) =>
        date.ToString(Get("date.shortWithTime"), CultureInfo.InvariantCulture);

    /// <summary>
    /// A date with the month in words. The month names come from the table, since the application
    /// runs with invariant globalization and the runtime knows only the English ones.
    /// </summary>
    public static string FormatLongDate(DateTime date) =>
        Format("date.long", ("day", date.Day), ("month", Get($"date.month.{date.Month}")), ("year", date.Year));

    /// <summary>
    /// The text of a key as a value that follows the language: it gives the current text on
    /// subscription and a new one after every change. Subscribers are held weakly, so a control
    /// bound to it is collected without unsubscribing.
    /// </summary>
    public static IObservable<string> Observe(string key) => Track(new TextObservable(() => Get(key)));

    /// <summary>
    /// Same as <see cref="Observe"/> for a text that a language may lack, which then is null.
    /// </summary>
    public static IObservable<string?> ObserveOptional(string key) => Track(new TextObservable(() => Find(key)));

    private static string? Format(StringTable[] lookup, string key, IReadOnlyDictionary<string, object?> arguments)
    {
        foreach (var table in lookup)
        {
            if (table.Find(key) is not { } text)
                continue;

            // Most texts are plain: neither a placeholder nor an escaped brace
            if (!text.Contains('{') && !text.Contains('\''))
                return text;

            try
            {
                return table.Format(text, arguments);
            }
            catch (Exception)
            {
                // A message that cannot be formatted is shown as written rather than taking the window down
                return text;
            }
        }

        return null;
    }

    private static TextObservable Track(TextObservable observable)
    {
        lock (Observables)
        {
            // Every bound control adds an entry, and nothing else removes the ones of collected controls
            if (Observables.Count >= _cleanupAt)
            {
                Observables.RemoveAll(reference => !reference.TryGetTarget(out _));
                _cleanupAt = Math.Max(MinObservablesBeforeCleanup, Observables.Count * 2);
            }

            Observables.Add(new WeakReference<TextObservable>(observable));
        }

        return observable;
    }

    private static StringTable? FindLanguage(string? code) =>
        code == null ? null : Languages.FirstOrDefault(language => language.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    // "pt-BR" and "pt_BR" give "pt"
    private static string? BaseCode(string? code)
    {
        var end = code?.IndexOfAny(['-', '_']) ?? -1;
        return end > 0 ? code![..end] : null;
    }

    private static IReadOnlyList<StringTable> LoadLanguages()
    {
        var assembly = typeof(Strings).Assembly;
        var languages = new List<StringTable>();

        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.EndsWith(LanguageFileExtension, StringComparison.Ordinal))
                continue;

            using var stream = assembly.GetManifestResourceStream(resource)!;
            languages.Add(StringTable.Load(stream, resource));
        }

        return languages
            .OrderBy(language => language.Code == InvariantCode ? 0 : 1)
            .ThenBy(language => language.Name, StringComparer.Ordinal)
            .ToArray();
    }
}
