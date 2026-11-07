using Client.Services.Chat;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Views.Sessions.Chat;

public class ChatAudioPlayer : Client.Views.Shared.Chat.ChatAudioPlayer
{
    public ChatAudioPlayer() : this(App.Services?.GetService<ChatAudioSession>()) { }

    internal ChatAudioPlayer(ChatAudioSession? session) : base(session) { }
}