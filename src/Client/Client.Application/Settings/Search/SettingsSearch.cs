namespace Client.Application.Settings.Search;

/// <summary>
/// Finds settings by a typed query. Every word of the query has to occur in the setting, in its
/// name, description or section, ignoring case. A match in the name outweighs one in the section,
/// which outweighs one in the description; a word that starts a word of the name counts the most.
/// </summary>
public static class SettingsSearch
{
    private const int TitleWordStartScore = 8;
    private const int TitleScore = 6;
    private const int SectionScore = 3;
    private const int DescriptionScore = 1;

    /// <returns>The matching items, the best first; items that match equally keep their original order. Empty for a blank query.</returns>
    public static IReadOnlyList<T> Find<T>(string? query, IEnumerable<T> items, Func<T, SettingSearchEntry> entryOf)
    {
        var words = (query ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return [];

        var matches = new List<(T Item, int Score, int Index)>();
        var index = 0;
        foreach (var item in items)
        {
            var score = Score(entryOf(item), words);
            if (score > 0)
                matches.Add((item, score, index));
            index++;
        }

        matches.Sort((a, b) => a.Score != b.Score ? b.Score.CompareTo(a.Score) : a.Index.CompareTo(b.Index));

        var result = new T[matches.Count];
        for (var i = 0; i < result.Length; i++)
            result[i] = matches[i].Item;
        return result;
    }

    private static int Score(SettingSearchEntry entry, string[] words)
    {
        var total = 0;
        foreach (var word in words)
        {
            var score = Score(entry.Text, word);
            if (entry.InvariantText != null)
                score = Math.Max(score, Score(entry.InvariantText, word));

            if (score == 0)
                return 0;
            total += score;
        }

        return total;
    }

    private static int Score(SettingSearchText text, string word)
    {
        if (StartsWord(text.Title, word))
            return TitleWordStartScore;
        if (Contains(text.Title, word))
            return TitleScore;
        if (Contains(text.Section, word))
            return SectionScore;
        if (text.Description != null && Contains(text.Description, word))
            return DescriptionScore;
        return 0;
    }

    // Ordinal: the client runs with invariant globalization, and the query is typed in the language of the texts
    private static bool Contains(string text, string word) => text.Contains(word, StringComparison.OrdinalIgnoreCase);

    private static bool StartsWord(string text, string word)
    {
        for (var at = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
             at >= 0;
             at = text.IndexOf(word, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            if (at == 0 || !char.IsLetterOrDigit(text[at - 1]))
                return true;
        }

        return false;
    }
}
