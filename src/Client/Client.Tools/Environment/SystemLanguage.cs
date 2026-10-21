using System.Runtime.InteropServices;

namespace Client.Tools.Environment;

/// <summary>
/// The language the user set for the operating system. It is read from the system directly:
/// the application runs with invariant globalization, where the current culture tells nothing.
/// </summary>
public static partial class SystemLanguage
{
    private const int LocaleNameMaxLength = 85;

    /// <returns>A two-letter language code such as "ru", or null when the system does not tell one.</returns>
    public static string? GetCode()
    {
        if (OperatingSystem.IsWindows())
        {
            var buffer = new char[LocaleNameMaxLength];
            var length = GetUserDefaultLocaleName(buffer, buffer.Length);
            return length > 1 ? ParseLocale(new string(buffer, 0, length - 1)) : null;
        }

        // The order gettext looks the variables up in
        foreach (var variable in (string[])["LANGUAGE", "LC_ALL", "LC_MESSAGES", "LANG"])
        {
            var value = System.Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            // LANGUAGE is a list of preferences: "ru:en_US"
            foreach (var locale in value.Split(':', StringSplitOptions.RemoveEmptyEntries))
            {
                if (ParseLocale(locale) is { } code)
                    return code;
            }
        }

        // Android sets none of the variables and runs with globalization, so there the culture does tell
        return ParseLocale(System.Globalization.CultureInfo.CurrentUICulture.Name);
    }

    /// <summary>
    /// Takes the language out of a locale name: "ru_RU.UTF-8" and "ru-RU" give "ru".
    /// </summary>
    /// <returns>Null for the locales that name no language: "C", "POSIX" or a blank one.</returns>
    public static string? ParseLocale(string? locale)
    {
        var text = locale?.Trim() ?? string.Empty;
        var end = text.IndexOfAny(['_', '-', '.', '@']);
        var language = (end < 0 ? text : text[..end]).ToLowerInvariant();

        if (language.Length is < 2 or > 3 || !language.All(char.IsAsciiLetter) || language == "posix")
            return null;

        return language;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetUserDefaultLocaleName([Out] char[] localeName, int size);
}
