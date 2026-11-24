using Client.Services;
using Client.Views.Shared.Common;

namespace Client.Mobile.Views.Sessions.Chat;

public class ChatAudioPlayer : Client.Views.Shared.Chat.ChatAudioPlayer
{
    public ChatAudioPlayer() : this(ViewServices.Get<IChatAudioSession>()) { }

    internal ChatAudioPlayer(IChatAudioSession? session) : base(session) { ApplyVolumeWhileDragging = true; }
}