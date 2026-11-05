using System;
using Avalonia.Threading;
using Client.Application.Chat;

namespace Client.ViewModels.Sessions.Chat;

public sealed partial class ChatComposerVM
{
    private readonly ChatTypingActivity _typing = new();
    private IDisposable? _typingTimeout;
    private bool _restoringText;
    private bool _typingDisposed;

    private void OnTypingInputChanged()
    {
        if (_restoringText || _typingDisposed)
            return;
        if (!IsConnected || !CanWrite || IsMessageSending)
        {
            StopTyping();
            return;
        }

        SendTyping(_typing.InputChanged(DateTimeOffset.UtcNow, !string.IsNullOrWhiteSpace(InputtedText)));
        _typingTimeout?.Dispose();
        _typingTimeout = DispatcherTimer.RunOnce(
            () => SendTyping(_typing.Expire(DateTimeOffset.UtcNow)), ChatTypingActivity.Timeout);
    }

    public void StopTyping()
    {
        _typingTimeout?.Dispose();
        _typingTimeout = null;
        SendTyping(_typing.Stop());
    }

    private void SendTyping(bool? state)
    {
        if (state is { } isTyping && IsConnected)
            _ = _session.SetChatTyping(_chatId, isTyping);
    }

    private void SetRestoredText(string text)
    {
        StopTyping();
        _restoringText = true;
        try { InputtedText = text; }
        finally { _restoringText = false; }
    }
}
