using Shared.Permissions;
using Client.Api.Mappers;
using Shared.Enums;
using Shared.Enums.Auth;
using Shared.Enums.User;
using Shared.Models;
using Shared.Models.Requests;
using Shared.Models.Requests.Auth;
using Shared.Models.Requests.Coordinator;
using Shared.Models.Requests.Heartbeat;
using Shared.Models.Requests.SessionActions.ChatSession;
using Shared.Models.Requests.SessionActions.Generic;
using Shared.Models.Responses;
using Shared.Models.Responses.Auth;
using Shared.Models.Responses.Coordinator;
using Shared.Models.Responses.Heartbeat;
using Shared.Models.Responses.SessionActions.ChatSession;
using Shared.Models.Responses.SessionActions.Generic;
using Shared.Validation;
using Client.Core.Chat;
using Client.Core.Coordinator;
using Client.Core.Push;
using Client.Core.Sessions;
using Client.Core.Sessions.Hybrid;
using Client.Core.Users;
using Client.Application.Ports;

namespace Client.Api;

internal partial class CoordinatorGateway
{
    public async Task<AuthenticationResult> AuthenticateAsync(
        string? password, string? userIdentity)
    {
        var request = new UserAuthRequest(password, userIdentity);
        var authResponse = await SendRequestAsync<UserAuthResponse>(request);

        return new AuthenticationResult(
            authResponse.AuthResult,
            authResponse.User?.ToModel(),
            authResponse.UserIdentity,
            authResponse.AuthResult == AuthResult.SecondStageRequired
                ? AccountSecurityMapper.ToLoginChallenge(authResponse.SecondStages, authResponse.FIDO2LoginOptions)
                : null);
    }

    public async Task<AuthenticationResult> FinishLoginAsync(string? password, string? twoFactorCode)
    {
        var request = new TwoFactorUserAuthRequest(password, twoFactorCode);
        var response = await SendRequestAsync<TwoFactorUserAuthResponse>(request);

        return new AuthenticationResult(AuthResult.Success, response.User.ToModel(), null);
    }

    public async Task<CoordinatorSessionModel?> GetCoordinatorInfo()
    {
        var request = new CoordinatorInfoRequest();
        var coordinatorInfoResponse = await SendRequestAsync<CoordinatorInfoResponse>(request);

        return coordinatorInfoResponse.Coordinator.ToModel();
    }

    public async Task<CoordinatorSettingsSnapshot> GetCoordinatorSettings()
    {
        var response = await SendRequestAsync<CoordinatorSettingsResponse>(new CoordinatorSettingsRequest());

        return new CoordinatorSettingsSnapshot(response.Settings.ToModel(),
            response.SFUNodes.Select(node => node.ToModel()).ToList());
    }

    public async Task<CoordinatorSettingsModel> UpdateCoordinatorSettings(CoordinatorSettingsUpdate update,
        long expectedRevision)
    {
        var request = new CoordinatorUpdateSettingsRequest(update.ToDto(), expectedRevision);
        var response = await SendRequestAsync<CoordinatorUpdateSettingsResponse>(request);

        return response.Settings.ToModel();
    }

    public async Task<IReadOnlyList<RoleModel>> GetRoles() =>
        (await SendRequestAsync<CoordinatorRolesResponse>(new CoordinatorRolesRequest())).Roles.ToModels();

    public async Task<RoleModel> CreateRole(RoleChange change) =>
        ToRole(await SendRequestAsync<CoordinatorRoleResponse>(new CoordinatorCreateRoleRequest(change.ToDto())));

    public async Task<RoleModel> UpdateRole(int roleId, RoleChange change, long expectedRevision)
    {
        var request = new CoordinatorUpdateRoleRequest(roleId, change.ToDto(), expectedRevision);

        return ToRole(await SendRequestAsync<CoordinatorRoleResponse>(request));
    }

    private static RoleModel ToRole(CoordinatorRoleResponse response) =>
        response.Role?.ToModel() ?? throw new UnexpectedResponseException("The role response carries no role");

    public Task DeleteRole(int roleId) =>
        SendRequestAsync<CoordinatorRoleResponse>(new CoordinatorDeleteRoleRequest(roleId));

    public async Task<IReadOnlyList<RoleModel>> ReorderRoles(IReadOnlyList<int> roleIds)
    {
        var response = await SendRequestAsync<CoordinatorRolesResponse>(
            new CoordinatorReorderRolesRequest(roleIds.ToList()));

        return response.Roles.ToModels();
    }

    public async Task<IReadOnlyList<int>> SetUserRole(int userId, int roleId, bool has)
    {
        Request request = has
            ? new CoordinatorAssignRoleRequest(userId, roleId)
            : new CoordinatorUnassignRoleRequest(userId, roleId);
        var response = await SendRequestAsync<CoordinatorUserRolesResponse>(request);

        return response.RoleIds.ToList();
    }

    public async Task<UserModel?> GetUserInfo(int userId)
    {
        var users = await GetUsersInfo([userId]);

        return users != null && users.TryGetValue(userId, out var user) ? user : null;
    }

    public async Task<Dictionary<int, UserModel>?> GetUsersInfo(List<int> userIds)
    {
        try
        {
            var response = await SendRequestAsync<CoordinatorUserInfoResponse>(new CoordinatorUserInfoRequest(userIds));

            return response.Users.ToDictionary(pair => pair.Key, pair => pair.Value.ToModel());
        }
        catch (RequestFailedException)
        {
            return null;
        }
    }

    public async Task<List<UserModel>> SearchUsers(string query, int limit)
    {
        try
        {
            var response = await SendRequestAsync<CoordinatorUserSearchResponse>(
                new CoordinatorUserSearchRequest(query, limit));

            return response.Users.Select(user => user.ToModel()).ToList();
        }
        catch (RequestFailedException)
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<UserModel>> GetAllUsers()
    {
        var response = await SendRequestAsync<CoordinatorUserListResponse>(new CoordinatorUserListRequest());

        return response.Users.Select(user => user.ToModel()).ToList();
    }

    public async Task<UserModel> SetUserState(int userId, UserState state)
    {
        var request = new CoordinatorSetUserStateRequest(userId, state);

        return (await SendRequestAsync<CoordinatorSetUserStateResponse>(request)).User.ToModel();
    }

    public Task DeleteUser(int userId) =>
        SendRequestAsync<CoordinatorDeleteUserResponse>(new CoordinatorDeleteUserRequest(userId));

    public async Task<UserModel> ChangeUsername(string username, int? userId = null)
    {
        var request = new CoordinatorChangeUsernameRequest(username, userId);

        return (await SendRequestAsync<CoordinatorChangeUsernameResponse>(request)).User.ToModel();
    }

    public async Task<UserModel> SetAvatar(string? uploadId, int? userId = null)
    {
        var request = new CoordinatorSetAvatarRequest(uploadId, userId);

        return (await SendRequestAsync<CoordinatorSetAvatarResponse>(request)).User.ToModel();
    }

    public async Task<UserModel> SetStatus(UserStatus status)
    {
        var request = new CoordinatorSetUserStatusRequest(status);

        return (await SendRequestAsync<CoordinatorSetUserStatusResponse>(request)).User.ToModel();
    }

    public async Task<string?> RegisterPushToken(string token, PushPlatform platform, PushKey key)
    {
        var request = new RegisterPushTokenRequest(token, platform, key.KeyId, Convert.ToBase64String(key.Key));

        return (await SendRequestAsync<RegisterPushTokenResponse>(request)).ReplyToken;
    }

    public Task UnregisterPushToken(string token) =>
        SendRequestAsync<UnregisterPushTokenResponse>(new UnregisterPushTokenRequest(token));

    public async Task<SessionModel> GetSessionInfo(int sessionId)
    {
        var response = await SendRequestAsync<SessionInfoResponse>(new SessionInfoRequest(sessionId));

        return response.Sessions.TryGetValue(sessionId, out var session)
            ? session.ToModel()
            : throw new UnexpectedResponseException($"The info of session {sessionId} does not carry it");
    }

    public async Task<(IDictionary<int, SessionModel> Sessions, SessionLayout Layout)> GetAllSessions()
    {
        var response = await SendRequestAsync<SessionInfoResponse>(new SessionInfoRequest(null));

        return (response.Sessions.ToDictionary(pair => pair.Key, pair => pair.Value.ToModel()),
            response.Layout?.ToModel() ?? throw new UnexpectedResponseException("The session list carries no layout"));
    }

    public Task CreateSession(NewSessionModel newSession) =>
        SendRequestAsync<CreateSessionResponse>(new CreateSessionRequest(newSession.ToDto()));

    public Task DeleteSession(int sessionId) =>
        SendRequestAsync<DeleteSessionResponse>(new DeleteSessionRequest(sessionId));

    public async Task<int> CreateSessionCategory(string name)
    {
        var response = await SendRequestAsync<SessionLayoutResponse>(new CreateSessionCategoryRequest(name));

        return response.CategoryId ?? throw new UnexpectedResponseException("The new category has no id");
    }

    public Task RenameSessionCategory(int categoryId, string name) =>
        SendRequestAsync<SessionLayoutResponse>(new RenameSessionCategoryRequest(categoryId, name));

    public Task DeleteSessionCategory(int categoryId) =>
        SendRequestAsync<SessionLayoutResponse>(new DeleteSessionCategoryRequest(categoryId));

    public Task MoveSession(int sessionId, int? categoryId, int index) =>
        SendRequestAsync<SessionLayoutResponse>(new MoveSessionRequest(sessionId, categoryId, index));

    public Task MoveSessionCategory(int categoryId, int index) =>
        SendRequestAsync<SessionLayoutResponse>(new MoveSessionCategoryRequest(categoryId, index));

    public async Task<ChatHistoryPage> RequestChatHistoryPage(int sessionId, int? beforeMessageId, int take)
    {
        var request = new ChatSessionHistoryPageRequest(sessionId, beforeMessageId, take);
        var response = await SendRequestAsync<ChatSessionHistoryPageResponse>(request);

        return new ChatHistoryPage(response.Messages.Select(message => message.ToModel()).ToList(),
            response.CurrentRevision);
    }

    public async Task<ChatChanges> RequestChatChanges(int sessionId, long afterRevision, int take)
    {
        var request = new ChatSessionChangesRequest(sessionId, afterRevision, take);
        var response = await SendRequestAsync<ChatSessionChangesResponse>(request);

        return new ChatChanges(response.Messages.Select(message => message.ToModel()).ToList(),
            response.CurrentRevision, response.HasMore, response.Reset);
    }

    public async Task<IReadOnlyList<ChatReadState>> RequestChatReadStates()
    {
        var response = await SendRequestAsync<ChatReadStatesResponse>(new ChatReadStatesRequest());

        return response.States.Select(state => state.ToModel()).ToList();
    }

    public async Task<ChatReadState> MarkChatRead(int sessionId, int messageId)
    {
        var response = await SendRequestAsync<ChatMarkReadResponse>(new ChatMarkReadRequest(sessionId, messageId));

        return response.State.ToModel();
    }

    public async Task<ChatMessageModel> RequestChatMessage(int sessionId, int messageId)
    {
        var request = new ChatSessionMessageRequest(sessionId, messageId);

        return (await SendRequestAsync<ChatSessionMessageResponse>(request)).Message.ToModel();
    }

    public Task SendChatMessage(int sessionId, NewChatMessage newMessage) =>
        SendRequestAsync<SendMessageResponse>(new SendMessageRequest(sessionId, newMessage.ToDto()));

    public Task DeleteChatMessage(int sessionId, int messageId) =>
        SendRequestAsync<DeleteMessageResponse>(new DeleteMessageRequest(sessionId, messageId));

    public Task EditChatMessage(int sessionId, int messageId, string text) =>
        SendRequestAsync<EditMessageResponse>(new EditMessageRequest(sessionId, messageId, text));

    public Task LeaveSession(int sessionId) =>
        SendRequestAsync<LeaveSessionResponse>(new LeaveSessionRequest(sessionId, SessionType.Hybrid));

    public Task KickFromHybridSession(int sessionId, int userId) =>
        SendRequestAsync<KickFromSessionResponse>(new KickFromSessionRequest(sessionId, userId, SessionType.Hybrid, null));

    public Task ApproveInHybridSession(int sessionId, int userId) =>
        SendRequestAsync<ApproveUserResponse>(new ApproveUserRequest(sessionId, userId, SessionType.Hybrid));

    public Task RejectInHybridSession(int sessionId, int userId) =>
        SendRequestAsync<RejectUserResponse>(new RejectUserRequest(sessionId, userId, SessionType.Hybrid, null));

    public Task ChangeHybridSessionSettings(int sessionId, string name, int capacity, bool isHostMustApprove,
        MediaSettings media) =>
        SendRequestAsync<ChangeHybridSessionSettingsResponse>(new ChangeHybridSessionSettingsRequest(sessionId, name,
            capacity, isHostMustApprove, media.ToDto()));

    public Task ChangeSessionPermissions(int sessionId, IReadOnlyList<PermissionOverride> overrides) =>
        SendRequestAsync<ChangeSessionPermissionsResponse>(
            new ChangeSessionPermissionsRequest(sessionId, overrides.ToDTO()));

    private Task SendPingAsync() =>
        SendRequestAsync<HeartbeatPingResponse>(new HeartbeatPingRequest());

    /// <exception cref="UnexpectedResponseException">The server answered with another response type.</exception>
    private async Task<TResponse> SendRequestAsync<TResponse>(Request request) where TResponse : Response
    {
        var response = await SendRequestAsync(request);

        return response as TResponse ?? throw new UnexpectedResponseException(
            $"{request.GetType().Name} was answered with {response.GetType().Name} instead of {typeof(TResponse).Name}");
    }

    private Task<BaseMessage> SendRequestAsync(Request request)
    {
        // The server rejects the same things; checking here saves the round trip and the strike
        if (!MessageValidator.TryValidate(request, out var errors))
        {
            var details = string.Join("; ", errors);
            LogInvalidRequestNotSent(request.GetType().Name, details);
            return Task.FromException<BaseMessage>(new RequestFailedException(ErrorCode.InvalidRequest, details));
        }

        return _responseAwaiter.SendAndWaitAsync(request.RequestId, () =>
        {
            // Fails at once instead of waiting out the response timeout on a dead connection
            if (!_webSocketGateway.SendMessage(request))
                throw new ConnectionLostException("Not connected to the coordinator");
        });
    }
}
