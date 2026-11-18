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

        // An application started from Finder or the Dock gets no locale variables at all
        if (OperatingSystem.IsMacOS() && GetMacPreferredLanguage() is { } preferred)
            return preferred;

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

    /// <returns>The first language of System Settings → Language &amp; Region, "ru-RU" giving "ru".</returns>
    private static string? GetMacPreferredLanguage()
    {
        var languages = CFLocaleCopyPreferredLanguages();
        if (languages == IntPtr.Zero)
            return null;

        try
        {
            if (CFArrayGetCount(languages) == 0)
                return null;

            // Owned by the array, not released here
            var first = CFArrayGetValueAtIndex(languages, 0);
            var buffer = new byte[64];
            return CFStringGetCString(first, buffer, buffer.Length, CFStringEncodingUtf8)
                ? ParseLocale(System.Text.Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0)))
                : null;
        }
        finally
        {
            CFRelease(languages);
        }
    }

    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint CFStringEncodingUtf8 = 0x08000100;

    [LibraryImport(CoreFoundation)]
    private static partial IntPtr CFLocaleCopyPreferredLanguages();

    [LibraryImport(CoreFoundation)]
    private static partial nint CFArrayGetCount(IntPtr array);

    [LibraryImport(CoreFoundation)]
    private static partial IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);

    [LibraryImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFStringGetCString(IntPtr text, [Out] byte[] buffer, nint size, uint encoding);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(IntPtr value);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetUserDefaultLocaleName([Out] char[] localeName, int size);
}
