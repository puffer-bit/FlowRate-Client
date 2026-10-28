namespace Client.Droid.Platform.Calls;

/// <summary>
/// What the notification of a voice session shows.
/// </summary>
/// <param name="Since">When the session was entered, in Unix milliseconds: the notification counts the time from it.</param>
internal sealed record CallState(string SessionName, bool IsMicrophoneOn, bool IsSoundOn, long Since)
{
    /// <summary>Shown for the moment a service that was started too late needs to stop.</summary>
    public static readonly CallState Leaving = new(string.Empty, false, false, 0);
}

/// <summary>
/// A button of the notification.
/// </summary>
internal enum CallAction
{
    ToggleMicrophone,
    Leave
}
