using Shared.Enums;
using Client.Core.Chat;
using Shared.Enums.User;
using Shared.Validation;
using Client.Core.Settings;
using Client.Core.Users;

namespace Client.Application.Coordinator;

public partial class CoordinatorSession
{
    // Set by every successful login
    private UserModel? _currentUser;
    private SavedUserModel? _currentUserCredentials;

    public Task<UserModel?> GetUser(int userId) =>
        _usersDirectory.GetUserAsync(userId);

    public Task<IReadOnlyDictionary<int, UserModel>> GetUsers(IReadOnlyCollection<int> userIds) =>
        _usersDirectory.GetUsersAsync(userIds);

    public bool IsUserDeleted(int userId) => _usersDirectory.IsDeleted(userId);

    public Task<IReadOnlyList<UserModel>> SearchUsers(string query, int limit) =>
        _usersDirectory.SearchAsync(query, limit);

    public Task<(UsersRequestResult Result, IReadOnlyList<UserModel>? Users)> GetAllUsers() =>
        SendAsync(_gateway.GetAllUsers, UsersRequestResult.Rejected, UsersRequestResult.TimedOut);

    public UserModel GetCurrentUser() =>
        _currentUser ?? throw new InvalidOperationException("Not logged in to the coordinator");

    public SavedUserModel GetCurrentUserCredentials() =>
        _currentUserCredentials ?? throw new InvalidOperationException("Not logged in to the coordinator");

    public Task<ChangeUsernameResult> ChangeUsername(string username) =>
        ChangeUsername(null, username);

    public Task<ChangeUsernameResult> ChangeUsername(int userId, string username) =>
        ChangeUsername((int?)userId, username);

    private async Task<ChangeUsernameResult> ChangeUsername(int? userId, string username)
    {
        if (!ValidateUsername(username))
            return ChangeUsernameResult.InvalidUsername;

        var (result, _) = await SendAsync(() => _gateway.ChangeUsername(username.Trim(), userId),
            ChangeUsernameResult.Rejected, ChangeUsernameResult.TimedOut);
        
        return result;
    }

    public async Task<ModerateUserResult> SetUserState(int userId, UserState state)
    {
        var (result, _) = await SendAsync(() => _gateway.SetUserState(userId, state),
            ModerateUserResult.Rejected, ModerateUserResult.TimedOut);

        return result;
    }

    public Task<ModerateUserResult> DeleteUser(int userId) =>
        SendAsync(() => _gateway.DeleteUser(userId), ModerateUserResult.Rejected, ModerateUserResult.TimedOut);

    public async Task<(SetAvatarResult Result, UserModel? User)> SetAvatar(byte[] image, string mimeType,
        CancellationToken cancellationToken = default)
    {
        if (image.Length is 0 or > Limits.AvatarMaxSize)
            return (SetAvatarResult.UploadFailed, null);

        var upload = await _gateway.UploadMedia(new MediaUpload(null, MediaUploadKind.Avatar, mimeType, image.Length,
            () => Task.FromResult<Stream>(new MemoryStream(image, writable: false))), null, cancellationToken);
        if (upload is not { Result: MediaTransferResult.NoError, UploadId: { } uploadId })
            return (SetAvatarResult.UploadFailed, null);

        return await SendAsync(() => _gateway.SetAvatar(uploadId),
            SetAvatarResult.Rejected, SetAvatarResult.TimedOut);
    }

    public async Task<SetUserStatusResult> SetStatus(UserStatus status)
    {
        var (result, _) = await SendAsync(() => _gateway.SetStatus(status),
            SetUserStatusResult.Rejected, SetUserStatusResult.TimedOut);

        return result;
    }

    public async Task<SetAvatarResult> ResetAvatar(int userId)
    {
        var (result, _) = await SendAsync(() => _gateway.SetAvatar(null, userId),
            SetAvatarResult.Rejected, SetAvatarResult.TimedOut);

        return result;
    }

    public bool ValidateUsername(string username)
    {
        // Mirrors the server's checks on CoordinatorChangeUsernameRequest
        var trimmed = username.Trim();
        return trimmed.Length > 0
               && trimmed.Length <= Limits.UsernameMaxLength
               && !trimmed.Any(char.IsControl);
    }

    private void OnUserUpdated(UserModel user)
    {
        _usersDirectory.Upsert(user);

        if (user.Id == _currentUser?.Id)
            SetCurrentUser(user, _currentUserCredentials!.UserIdentity);

        UserUpdated?.Invoke(user);
    }

    private void OnUserDeleted(int userId)
    {
        _usersDirectory.Forget(userId);
        UserDeleted?.Invoke(userId);
    }

    private void SetCurrentUser(UserModel user, string userIdentity)
    {
        _currentUser = user;
        _currentUserCredentials = new SavedUserModel
        {
            Id = user.Id,
            UserIdentity = userIdentity,
            Username = user.Username,
        };

        // The roles of the user may have changed with it
        UpdatePermissions();
        CurrentUserChanged?.Invoke(user);
    }
}
