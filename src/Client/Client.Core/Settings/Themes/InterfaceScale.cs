namespace Client.Core.Settings.Themes;

/// <summary>
/// The size the interface is drawn at, in percent of its usual size.
/// </summary>
public static class InterfaceScale
{
    public const int Min = 80;
    public const int Max = 150;
    public const int Step = 10;
    public const int Default = 100;

    /// <summary>
    /// The choices of the settings, from the smallest to the largest.
    /// </summary>
    public static IReadOnlyList<int> Choices { get; } =
        Enumerable.Range(0, (Max - Min) / Step + 1).Select(index => Min + index * Step).ToArray();

    /// <summary>
    /// The choice closest to <paramref name="percent"/>: a stored value may come from a build with other choices.
    /// </summary>
    public static int Normalize(int percent)
    {
        var steps = (int)Math.Round((Math.Clamp(percent, Min, Max) - Min) / (double)Step);
        return Min + steps * Step;
    }
}
