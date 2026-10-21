namespace Client.Services;

/// <summary>
/// Types of icons available in the application
/// </summary>
public enum IconType
{
    // Status icons
    Warning,
    KeyWarning,
    KeyQuestion,
    Error,
    Info,
    Success,
    Question,
    Stop,
    
    // User related
    BaseUser,
    BaseUsers,
    
    // Session related
    VideoSession,
    ConnectToInstance,
    
    // Action icons
    Save,
    Delete,
    New,
    Find,
    Reset,
    
    // Security
    Shield,
    GoodShield,
    BadShield,
    StrangeShield,
    
    // Media
    Microphone,
    MicrophoneMuted,
    MaxVolume,
    
    // UI Controls
    ShowPassword,
    HidePassword,
    CloseNotification,
    GoFullscreen,
    GoLive,
    
    // States
    Completed,
    Failed,
    Joined,
    NotJoined,
    
    // Server/Connection
    Server,
    Host,
    
    // Settings
    Settings,
    
    // Actions
    Logout,
    Login,
    KickFromSession,
    Crush,
    
    // Other
    Yes,
    No,
    Tip,
    Stats
}
