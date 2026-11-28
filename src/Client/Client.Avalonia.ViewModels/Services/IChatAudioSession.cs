using System.Threading.Tasks;
using Client.ViewModels.Sessions.Chat;

namespace Client.Services;

/// <summary>The playback operations used by the shared audio attachment control.</summary>
public interface IChatAudioSession : IChatMediaSession
{
    bool IsCurrent(ChatFileVM? file);
    Task InitializeAsync();
}