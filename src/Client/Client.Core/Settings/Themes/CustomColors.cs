namespace Client.Core.Settings.Themes;

/// <summary>
/// Colors the user has chosen instead of the ones of the theme, each as "#RRGGBB"; null keeps the
/// color of the theme. They are the same in the dark and in the light theme.
/// </summary>
public sealed record CustomColors
{
    public string? Primary { get; init; }
    public string? Secondary { get; init; }
    public string? Danger { get; init; }
    public string? Save { get; init; }
    public string? Success { get; init; }
    public string? Warning { get; init; }
    public string? Error { get; init; }
}
