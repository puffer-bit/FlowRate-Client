using Client.Application.Coordinator;
using Client.Core.Users;
using Client.ViewModels.Sessions.Chat;
using Shared.Models.DataTransferObjects;
using Xunit;

namespace Client.Application.Tests;

public class DirectCallPresentationTests
{
    [Theory]
    [InlineData(1, 2, "Alice", "Alice")]
    [InlineData(2, 1, "Bob", "Bob")]
    [InlineData(1, 2, null, "peer")]
    [InlineData(2, 1, "  ", "peer")]
    public async Task CallNameWaitsForPeerProfileWithoutDependingOnChatList(int currentUserId, int peerId,
        string? displayName, string expected)
    {
        var profile = new TaskCompletionSource<UserModel?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestedUsers = new List<int>();
        var coordinator = ChatHistoryWindowTests.Proxy<ICoordinatorSession>((method, args) => method switch
        {
            nameof(ICoordinatorSession.GetCurrentUser) => new UserModel
                { Id = currentUserId, Username = "me", CoordinatorInstanceId = "server" },
            nameof(ICoordinatorSession.GetUser) => GetPeer((int)args![0]!),
            _ => throw new NotSupportedException(method)
        });
        Task<UserModel?> GetPeer(int id)
        {
            requestedUsers.Add(id);
            return profile.Task;
        }

        var call = new DirectCallDTO(Guid.NewGuid(), 7, 1, 2, DirectCallState.Active, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, true, null);
        var name = DirectCallPresentation.GetPeerNameAsync(coordinator, call);
        Assert.False(name.IsCompleted);
        Assert.Equal(new[] { peerId }, requestedUsers);

        profile.SetResult(new UserModel
            { Id = peerId, Username = "peer", DisplayName = displayName, CoordinatorInstanceId = "server" });
        Assert.Equal(expected, await name);
    }
}
