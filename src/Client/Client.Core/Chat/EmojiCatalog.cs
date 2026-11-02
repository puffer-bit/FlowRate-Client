namespace Client.Core.Chat;

public sealed record EmojiEntry(string Text, int Category, string EnglishName, string RussianName, string Keywords)
{
    public string Name(bool russian) => russian ? RussianName : EnglishName;
}

/// <summary>The offline Unicode 16.0 catalog, with English and Russian CLDR 46 annotations.</summary>
public static class EmojiCatalog
{
    private static readonly Lazy<IReadOnlyList<EmojiEntry>> Catalog = new(Load);
    public static IReadOnlyList<EmojiEntry> Entries => Catalog.Value;

    // A query searches all categories; the category applies when browsing without a query.
    public static IReadOnlyList<EmojiEntry> Search(string? query, int? category = null)
    {
        var words = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return Entries.Where(entry => words.Length == 0
                ? category == null || entry.Category == category
                : words.All(word => entry.Text.Contains(word, StringComparison.Ordinal)
                    || entry.Keywords.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private static IReadOnlyList<EmojiEntry> Load()
    {
        using var stream = typeof(EmojiCatalog).Assembly.GetManifestResourceStream("Client.Core.Chat.EmojiData.catalog.tsv")!;
        using var reader = new StreamReader(stream);
        List<EmojiEntry> entries = [];
        while (reader.ReadLine() is { } line)
        {
            var fields = line.Split('\t');
            entries.Add(new EmojiEntry(fields[0], int.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture),
                fields[2], fields[3], string.Join(' ', fields[2..])));
        }
        return entries.AsReadOnly();
    }
}

/// <summary>A bounded, most-recent-first list; unknown entries from older catalogs are ignored.</summary>
public sealed class RecentEmoji
{
    public const int Capacity = 32;
    private readonly List<EmojiEntry> _entries = [];
    public IReadOnlyList<EmojiEntry> Entries => _entries.AsReadOnly();

    public RecentEmoji(IEnumerable<string>? saved = null)
    {
        if (saved == null)
            return;
        var known = EmojiCatalog.Entries.ToDictionary(entry => entry.Text, StringComparer.Ordinal);
        foreach (var text in saved.Distinct(StringComparer.Ordinal).Take(Capacity))
            if (known.TryGetValue(text, out var entry))
                _entries.Add(entry);
    }

    public void Record(EmojiEntry entry)
    {
        _entries.RemoveAll(recent => recent.Text == entry.Text);
        _entries.Insert(0, entry);
        if (_entries.Count > Capacity)
            _entries.RemoveRange(Capacity, _entries.Count - Capacity);
    }
}
