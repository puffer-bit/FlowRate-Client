using Shared.Enums;
using Client.Core.Chat;
using Client.Core.Coordinator;
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

    public Task<RequestResult<IReadOnlyList<UserModel>>> GetAllUsers() =>
        SendAsync(_gateway.GetAllUsers);

    public UserModel GetCurrentUser() =>
        _currentUser ?? throw new InvalidOperationException("Not logged in to the coordinator");

    public SavedUserModel GetCurrentUserCredentials() =>
        _currentUserCredentials ?? throw new InvalidOperationException("Not logged in to the coordinator");

    public Task<RequestResult> ChangeUsername(string username) =>
        ChangeUsername(null, username);

    public Task<RequestResult> ChangeUsername(int userId, string username) =>
        ChangeUsername((int?)userId, username);

    private async Task<RequestResult> ChangeUsername(int? userId, string username)
    {
        if (!ValidateUsername(username))
            return ErrorCode.InvalidValue;

        return (await SendAsync(() => _gateway.ChangeUsername(username.Trim(), userId))).Result;
    }

    public async Task<RequestResult> SetUserState(int userId, UserState state) =>
        (await SendAsync(() => _gateway.SetUserState(userId, state))).Result;

    public Task<RequestResult> DeleteUser(int userId) =>
        SendAsync(() => _gateway.DeleteUser(userId));

    /// <summary>
    /// Fails with <see cref="ErrorCode.UploadNotFound"/> when the image is empty or too large, or did not get to the server.
    /// </summary>
    public async Task<RequestResult<UserModel>> SetAvatar(byte[] image, string mimeType,
        CancellationToken cancellationToken = default)
    {
        if (image.Length is 0 or > Limits.AvatarMaxSize)
            return ErrorCode.UploadNotFound;

        var upload = await _gateway.UploadMedia(new MediaUpload(null, MediaUploadKind.Avatar, mimeType, image.Length,
            () => Task.FromResult<Stream>(new MemoryStream(image, writable: false))), null, cancellationToken);
        if (upload is not { Result: MediaTransferResult.NoError, UploadId: { } uploadId })
            return ErrorCode.UploadNotFound;

        return await SendAsync(() => _gateway.SetAvatar(uploadId));
    }

    public async Task<RequestResult> SetStatus(UserStatus status) =>
        (await SendAsync(() => _gateway.SetStatus(status))).Result;

    public async Task<RequestResult> ResetAvatar(int userId) =>
        (await SendAsync(() => _gateway.SetAvatar(null, userId))).Result;

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
            SessionToken = _sessionToken
        };

        // The roles of the user may have changed with it
        UpdatePermissions();
        CurrentUserChanged?.Invoke(user);
    }
}
