using Client.Core.Users;

namespace Client.Application.Coordinator.Users;

/// <summary>
/// Volume, mute and overdrive of the peers of hybrid sessions, as heard on this client. The settings
/// are remembered per user and apply to every hybrid session the user is met in. The voice of a user
/// and the sound of the screen they show have settings of their own, see <see cref="PeerAudioChannel"/>.
/// </summary>
public interface IPeerPlaybackService
{
    /// <summary>
    /// Raised with the new settings of a channel of a user, both on a change and once the stored
    /// settings of a user are loaded, then for each channel. Raised on any thread.
    /// </summary>
    event Action<int, PeerAudioChannel, PeerPlayback>? Changed;

    /// <summary>
    /// Loads the stored settings of peers added to a hybrid session. Users already known are
    /// skipped, and the settings of another coordinator are forgotten, since user ids are only
    /// unique within one. Never throws.
    /// </summary>
    Task LoadAsync(string coordinatorInstanceId, IReadOnlyCollection<int> userIds);

    /// <summary>
    /// The settings of a channel of a user, or the defaults while the stored ones are not loaded yet.
    /// </summary>
    PeerPlayback Get(int userId, PeerAudioChannel channel);

    void Set(int userId, PeerAudioChannel channel, PeerPlayback playback);
}
