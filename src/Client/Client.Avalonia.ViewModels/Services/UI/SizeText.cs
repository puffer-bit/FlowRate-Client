using System.Globalization;
using Client.Avalonia.Localization;

namespace Client.Services;

/// <summary>
/// A number of bytes as the interface writes it, in the unit that keeps the number short.
/// </summary>
public static class SizeText
{
    public static string Format(long bytes) => bytes switch
    {
        < 1024 => Strings.Format("size.bytes", ("size", bytes)),
        < 1024 * 1024 => Strings.Format("size.kilobytes", ("size", (bytes / 1024d).ToString("0.#", CultureInfo.InvariantCulture))),
        < 1024L * 1024 * 1024 => Strings.Format("size.megabytes", ("size", (bytes / (1024d * 1024)).ToString("0.#", CultureInfo.InvariantCulture))),
        _ => Strings.Format("size.gigabytes", ("size", (bytes / (1024d * 1024 * 1024)).ToString("0.##", CultureInfo.InvariantCulture)))
    };
}
