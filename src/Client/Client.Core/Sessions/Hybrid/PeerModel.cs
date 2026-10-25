using Shared.Enums;

namespace Client.Core.Sessions.Hybrid;

public class PeerModel
{
    public required int UserId { get; init; }
    public bool IsAudioRequested { get; init; }
    public bool IsNegotiated { get; init; }
    public bool IsConnected { get; init; }
    public bool IsApproved { get; init; }
    public bool IsRejected { get; init; }
    public HybridSessionPeerState State { get; set; }

    /// <summary>
    /// Set when the user turned their microphone off; the server sends it to everyone.
    /// </summary>
    public bool IsMicrophoneMuted { get; set; }

    /// <summary>
    /// Set when the user turned the sound of the session off and hears nobody; the server sends it
    /// to everyone.
    /// </summary>
    public bool IsSoundMuted { get; set; }

    /// <summary>
    /// Set while the user is away from the keyboard, with the reason they gave; the server sends it to everyone.
    /// </summary>
    public bool IsAfk { get; set; }
    public string? AfkReason { get; set; }

    /// <summary>
    /// Set while the user shows a screen; the server tells everyone, the track goes only to those
    /// who receive it.
    /// </summary>
    public bool IsScreenShareActive { get; set; }

    /// <summary>
    /// Set while the shown screen carries sound as well.
    /// </summary>
    public bool IsScreenAudioActive { get; set; }
}