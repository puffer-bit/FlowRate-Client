namespace Client.ViewModels.Sessions.Hybrid;

/// <summary>
/// Push to talk, and the warning that we talk while nobody hears it.
/// </summary>
public partial class ActiveHybridSessionVM
{
    /// <summary>
    /// Turns push to talk on while a key is assigned to it: the microphone, on as the others see it, sends
    /// only while the key is held.
    /// </summary>
    public void SetPushToTalkEnabled(bool isEnabled)
    {
        StatusBar.IsPushToTalkEnabled = isEnabled;
        _hybridSession.IsPushToTalkHeld = false;
        _hybridSession.IsPushToTalkEnabled = isEnabled;
    }

    /// <summary>The key of push to talk was pressed or released.</summary>
    public void SetPushToTalkHeld(bool isHeld)
    {
        if (_hybridSession.IsPushToTalkEnabled)
            _hybridSession.IsPushToTalkHeld = isHeld;
    }

    /// <summary>Which of the warnings that we talk while nobody hears it the user wants.</summary>
    public void SetMutedSpeechWarnings(bool microphone, bool pushToTalk)
    {
        StatusBar.IsMutedMicrophoneWarningEnabled = microphone;
        StatusBar.IsPushToTalkWarningEnabled = pushToTalk;
    }

    // The status bar decides whether it is worth a warning; it moves to the UI thread itself
    private void OnMutedSpeakingChanged(bool isSpeaking) => StatusBar.IsMutedSpeechDetected = isSpeaking;
}
