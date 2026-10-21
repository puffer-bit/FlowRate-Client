using System.Globalization;
using System.Text.Json;
using Jeffijoe.MessageFormat;

namespace Client.Avalonia.Localization;

/// <summary>
/// The interface texts of one language, read from an ARB file: a JSON object of keys and texts, where
/// the names that start with <c>@</c> describe a text for translators and are not texts themselves.
/// A text is an ICU message: <c>{name}</c> is replaced by an argument, and
/// <c>{count, plural, one{...} other{...}}</c> picks a form by the rules of the language.
/// </summary>
public sealed class StringTable
{
    private const string LocaleProperty = "@@locale";
    // Custom file attributes of ARB start with "@@x-"
    private const string TranslatedByProperty = "@@x-translatedBy";
    private const string TranslatedByAi = "ai";
    private const string NameKey = "language.name";

    private readonly Dictionary<string, string> _texts;
    private readonly MessageFormatter _formatter;

    private StringTable(string code, bool isMachineTranslated, Dictionary<string, string> texts)
    {
        Code = code;
        IsMachineTranslated = isMachineTranslated;
        _texts = texts;
        _formatter = new MessageFormatter(useCache: true, culture: FindCulture(code));
    }

    /// <summary>Code of the language as its file names it: "en", "ru", "pt-BR".</summary>
    public string Code { get; }

    /// <summary>The name of the language in the language itself, shown where a language is picked.</summary>
    public string Name => Find(NameKey) ?? Code;

    /// <summary>
    /// Whether the file says that nobody who speaks the language has checked its texts:
    /// <c>"@@x-translatedBy": "ai"</c>. The language is marked so where it is picked.
    /// </summary>
    public bool IsMachineTranslated { get; }

    public IReadOnlyCollection<string> Keys => _texts.Keys;

    /// <returns>The text as it is written in the file, or null when this language has no text for the key.</returns>
    public string? Find(string key) => _texts.GetValueOrDefault(key);

    /// <summary>
    /// Puts the arguments into a text of this language. The plural forms are picked by the rules of
    /// this language, so a text taken from English as a fallback is formatted by the English table.
    /// </summary>
    /// <exception cref="Exception">The text is not a valid message, or it names an argument that is not given.</exception>
    public string Format(string text, IReadOnlyDictionary<string, object?> arguments)
    {
        // The formatter caches parsed messages and is not thread safe
        lock (_formatter)
            return _formatter.FormatMessage(text, arguments);
    }

    /// <exception cref="InvalidDataException">The file is not an ARB file: the message tells what is wrong.</exception>
    public static StringTable Load(Stream stream, string source)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{source} is not valid JSON: {exception.Message}", exception);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"{source} must be a JSON object of keys and texts");

            string? code = null;
            var isMachineTranslated = false;
            var texts = new Dictionary<string, string>();

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name == LocaleProperty)
                {
                    code = property.Value.GetString();
                    continue;
                }

                if (property.Name == TranslatedByProperty)
                {
                    isMachineTranslated = property.Value.ValueKind == JsonValueKind.String
                                          && property.Value.GetString() == TranslatedByAi;
                    continue;
                }

                // "@key" describes the text of "key", "@@name" the file
                if (property.Name.StartsWith('@'))
                    continue;

                if (property.Value.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException($"{source}: the text of \"{property.Name}\" must be a string");

                // JSON allows a name twice and keeps the last one, which hides a mistake
                if (!texts.TryAdd(property.Name, property.Value.GetString()!))
                    throw new InvalidDataException($"{source}: \"{property.Name}\" is defined twice");
            }

            if (string.IsNullOrWhiteSpace(code))
                throw new InvalidDataException($"{source} has no \"{LocaleProperty}\"");

            return new StringTable(code, isMachineTranslated, texts);
        }
    }

    // The culture only names the language for the plural rules. The application runs with invariant
    // globalization, where a culture carries no data of its own
    private static CultureInfo FindCulture(string code)
    {
        try
        {
            return new CultureInfo(code);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
