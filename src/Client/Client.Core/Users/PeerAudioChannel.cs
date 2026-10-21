namespace Client.Core.Users;

/// <summary>
/// The sounds of a user that are played on their own settings: what the user says, and what the
/// screen they show plays. Turning one down or off leaves the other as it is.
/// </summary>
public enum PeerAudioChannel
{
    Voice = 0,
    Screen = 1
}
