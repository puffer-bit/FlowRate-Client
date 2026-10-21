namespace Client.Core.Settings.Themes;

/// <summary>
/// How the interface is animated. Loading indicators keep turning whatever is chosen here.
/// </summary>
public sealed record AnimationSettings
{
    public bool IsEnabled { get; init; } = true;
    public AnimationSpeed Speed { get; init; } = AnimationSpeed.Normal;
}

// Stored as a number, so the values must not be renumbered
public enum AnimationSpeed
{
    Slow = 0,
    Normal = 1,
    Fast = 2
}
