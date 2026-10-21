namespace Client.Core.Settings;

/// <summary>Which releases the application updates itself to.</summary>
public enum UpdateChannel
{
    /// <summary>Only the published releases.</summary>
    Release = 0,
    /// <summary>The development builds as well, published as pre-releases; a newer release still wins.</summary>
    Dev = 1,
}
