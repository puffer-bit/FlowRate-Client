namespace Client.Application.Sounds;

/// <summary>
/// What the interface makes a sound for. A sound pack names its files after these, see
/// <see cref="SoundFiles"/>.
/// </summary>
public enum SoundEvent
{
    Join,
    Leave,
    Connected,
    Disconnected,
    Message,
    MessageSent,
    Mute,
    Unmute,
    Deafen,
    Undeafen,
    ScreenShareStart,
    ScreenShareStop,
    Error,
    CallIncoming,
    Mention,
    UserKicked,
    Poke,
    Afk,
}
