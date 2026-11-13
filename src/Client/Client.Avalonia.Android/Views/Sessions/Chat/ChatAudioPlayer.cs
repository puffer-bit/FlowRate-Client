using Client.Droid.Platform.Media;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Droid.Views.Sessions.Chat;

public class ChatAudioPlayer : Client.Views.Shared.Chat.ChatAudioPlayer
{
    public ChatAudioPlayer() : this(App.Services?.GetService<ChatAudioSession>()) { }

    internal ChatAudioPlayer(ChatAudioSession? session) : base(session) { }
}