using Client.Application.Ports;
using Client.Core.Media;

namespace Client.ViewModels.Sessions.Hybrid;

/// <summary>
/// What the user asked for in the dialog that starts a screen share.
/// </summary>
/// <param name="Input">The screen or window to show.</param>
/// <param name="Settings">How to show it.</param>
/// <param name="AudioInput">The sound to send along, or null to show it silent.</param>
public sealed record ScreenShareRequest(VideoInput Input, ScreenShareSettings Settings, AudioInput? AudioInput = null);
