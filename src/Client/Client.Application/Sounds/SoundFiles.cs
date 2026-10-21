namespace Client.Application.Sounds;

/// <summary>
/// The file of every event, the same in the built-in sounds and in a sound pack.
/// </summary>
public static class SoundFiles
{
    public const string Extension = ".wav";

    // A switch and not Enum.ToString: these names are the format of a pack and do not follow a rename
    public static string GetName(SoundEvent soundEvent) => soundEvent switch
    {
        SoundEvent.Join => "join",
        SoundEvent.Leave => "leave",
        SoundEvent.Connected => "connected",
        SoundEvent.Disconnected => "disconnected",
        SoundEvent.Message => "message",
        SoundEvent.MessageSent => "message_sent",
        SoundEvent.Mute => "mute",
        SoundEvent.Unmute => "unmute",
        SoundEvent.Deafen => "deafen",
        SoundEvent.Undeafen => "undeafen",
        SoundEvent.ScreenShareStart => "screen_share_start",
        SoundEvent.ScreenShareStop => "screen_share_stop",
        SoundEvent.Error => "error",
        SoundEvent.CallIncoming => "call_incoming",
        SoundEvent.Mention => "mention",
        SoundEvent.UserKicked => "user_kicked",
        _ => throw new ArgumentOutOfRangeException(nameof(soundEvent), soundEvent, null),
    };

    /// <returns>False when no event has that name.</returns>
    public static bool TryGetEvent(string name, out SoundEvent soundEvent)
    {
        foreach (var candidate in Enum.GetValues<SoundEvent>())
        {
            if (GetName(candidate) != name)
                continue;

            soundEvent = candidate;
            return true;
        }

        soundEvent = default;
        return false;
    }

    public static string GetFileName(SoundEvent soundEvent) => GetName(soundEvent) + Extension;
}
