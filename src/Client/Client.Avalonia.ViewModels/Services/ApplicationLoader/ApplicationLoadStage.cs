namespace Client.Services;

/// <summary>What the start of the application is busy with, as the startup screen tells it.</summary>
public enum ApplicationLoadStage
{
    /// <summary>The database is unlocked, by the keyring or by the password of the user.</summary>
    Database,

    /// <summary>The database is checked while the native media libraries are loaded.</summary>
    Components,

    /// <summary>The stored settings are applied: language, theme, sounds, hot keys.</summary>
    Settings,
}
