using Client.Application.Coordinator;
using Client.ViewModels.Sessions.Chat;
using Xunit;

namespace Client.Application.Tests;

public class ChatComposerTests
{
    internal static ICoordinatorSession CreateSession() =>
        ChatHistoryWindowTests.Proxy<ICoordinatorSession>((name, _) =>
            name.StartsWith("add_") || name.StartsWith("remove_")
                ? null
                : throw new InvalidOperationException(name));

    [Fact]
    public Task MissingSessionIsRejectedBeforeSubscribing() => TestUiThread.RunAsync(() =>
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ChatComposerVM(7, "chat", false, null!, 1, null!, null!, null!, _ => Task.CompletedTask));
        Assert.Equal("session", exception.ParamName);
    });
}
