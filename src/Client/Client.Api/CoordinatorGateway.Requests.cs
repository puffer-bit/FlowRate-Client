using Shared.Permissions;
using Client.Api.Mappers;
using Shared.Enums;
using Shared.Enums.Coordinator;
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
            authResponse.UserDTO?.ToModel(),
            authResponse.UserIdentity);
    }

    public async Task<CoordinatorSessionModel?> GetCoordinatorInfo()
    {
        var request = new CoordinatorInfoRequest();
        var coordinatorInfoResponse = await SendRequestAsync<CoordinatorInfoResponse>(request);

        return coordinatorInfoResponse.CoordinatorSessionDto?.ToModel();
    }
    
    public async Task<(CoordinatorSettingsResult Result, CoordinatorSettingsSnapshot? Snapshot)>
        GetCoordinatorSettings()
    {
        var response = await SendRequestAsync<CoordinatorSettingsResponse>(new CoordinatorSettingsRequest());

        return response is { Result: CoordinatorSettingsResult.NoError, Settings: { } settings }
            ? (response.Result, new CoordinatorSettingsSnapshot(settings.ToModel(),
                response.SfuNodes?.Select(node => node.ToModel()).ToList() ?? []))
            : (response.Result, null);
    }

    public async Task<CoordinatorSettingsUpdateOutcome> UpdateCoordinatorSettings(CoordinatorSettingsUpdate update,
        long expectedRevision)
    {
        var request = new CoordinatorUpdateSettingsRequest(update.ToDto(), expectedRevision);
        var response = await SendRequestAsync<CoordinatorUpdateSettingsResponse>(request);

        return new CoordinatorSettingsUpdateOutcome(response.Result, response.Settings?.ToModel(),
            response.Errors?.ToDictionary() ?? new Dictionary<string, string[]>());
    }

    public async Task<(CoordinatorRoleResult Result, IReadOnlyList<RoleModel>? Roles)> GetRoles()
    {
        var response = await SendRequestAsync<CoordinatorRolesResponse>(new CoordinatorRolesRequest());

        return (response.Result, response.Roles?.ToModels());
    }

    public async Task<(CoordinatorRoleResult Result, RoleModel? Role)> CreateRole(RoleChange change)
    {
        var response = await SendRequestAsync<CoordinatorRoleResponse>(new CoordinatorCreateRoleRequest(change.ToDto()));

        return (response.Result, response.Role?.ToModel());
    }

    public async Task<(CoordinatorRoleResult Result, RoleModel? Role)> UpdateRole(int roleId, RoleChange change,
        long expectedRevision)
    {
        var request = new CoordinatorUpdateRoleRequest(roleId, change.ToDto(), expectedRevision);
        var response = await SendRequestAsync<CoordinatorRoleResponse>(request);

        return (response.Result, response.Role?.ToModel());
    }

    public async Task<CoordinatorRoleResult> DeleteRole(int roleId) =>
        (await SendRequestAsync<CoordinatorRoleResponse>(new CoordinatorDeleteRoleRequest(roleId))).Result;

    public async Task<(CoordinatorRoleResult Result, IReadOnlyList<RoleModel>? Roles)> ReorderRoles(
        IReadOnlyList<int> roleIds)
    {
        var response = await SendRequestAsync<CoordinatorRolesResponse>(
            new CoordinatorReorderRolesRequest(roleIds.ToList()));

        return (response.Result, response.Roles?.ToModels());
    }

    public async Task<(CoordinatorRoleResult Result, IReadOnlyList<int>? RoleIds)> SetUserRole(int userId,
        int roleId, bool has)
    {
        Request request = has
            ? new CoordinatorAssignRoleRequest(userId, roleId)
            : new CoordinatorUnassignRoleRequest(userId, roleId);
        var response = await SendRequestAsync<CoordinatorUserRolesResponse>(request);

        return (response.Result, response.RoleIds?.ToList());
    }

    public async Task<UserModel?> GetUserInfo(int userId)
    {
        var usersList = new List<int> { userId };
        var usersInfoRequest = new CoordinatorUserInfoRequest(usersList);
        
        var usersInfoResponse = await SendRequestAsync<CoordinatorUserInfoResponse>(usersInfoRequest);
        
        if (usersInfoResponse.Result is UsersRequestResult.NoError)
        {
            if (usersInfoResponse.UserList.TryGetValue(userId, out var userDTO))
            {
                return userDTO.ToModel();
            }
        }

        return null;
    }
    
    public async Task<Dictionary<int, UserModel>?> GetUsersInfo(List<int> userIds)
    {
        var usersInfoRequest = new CoordinatorUserInfoRequest(userIds);
        
        var usersInfoResponse = await SendRequestAsync<CoordinatorUserInfoResponse>(usersInfoRequest);
        
        if (usersInfoResponse.Result is UsersRequestResult.NoError)
        {
            return usersInfoResponse.UserList.ToDictionary(pair => pair.Key, pair => pair.Value.ToModel());
        }

        return null;
    }

    public async Task<List<UserModel>> SearchUsers(string query, int limit)
    {
        var request = new CoordinatorUserSearchRequest(query, limit);
        var response = await SendRequestAsync<CoordinatorUserSearchResponse>(request);

        return response.Result is UsersRequestResult.NoError
            ? response.Users.Select(user => user.ToModel()).ToList()
            : [];
    }

    public async Task<(UsersRequestResult Result, IReadOnlyList<UserModel>? Users)> GetAllUsers()
    {
        var response = await SendRequestAsync<CoordinatorUserListResponse>(new CoordinatorUserListRequest());

        return response.Result is UsersRequestResult.NoError
            ? (response.Result, response.Users.Select(user => user.ToModel()).ToList())
            : (response.Result, null);
    }

    public async Task<(ModerateUserResult Result, UserModel? User)> SetUserState(int userId, UserState state)
    {
        var request = new CoordinatorSetUserStateRequest(userId, state);
        var response = await SendRequestAsync<CoordinatorSetUserStateResponse>(request);

        return (response.Result, response.User?.ToModel());
    }

    public async Task<ModerateUserResult> DeleteUser(int userId)
    {
        var response = await SendRequestAsync<CoordinatorDeleteUserResponse>(new CoordinatorDeleteUserRequest(userId));
        return response.Result;
    }

    public async Task<(ChangeUsernameResult Result, UserModel? User)> ChangeUsername(string username, int? userId = null)
    {
        var request = new CoordinatorChangeUsernameRequest(username, userId);
        var response = await SendRequestAsync<CoordinatorChangeUsernameResponse>(request);

        return (response.Result, response.User?.ToModel());
    }

    public async Task<(SetAvatarResult Result, UserModel? User)> SetAvatar(string? uploadId, int? userId = null)
    {
        var request = new CoordinatorSetAvatarRequest(uploadId, userId);
        var response = await SendRequestAsync<CoordinatorSetAvatarResponse>(request);

        return (response.Result, response.User?.ToModel());
    }

    public async Task<(SetUserStatusResult Result, UserModel? User)> SetStatus(UserStatus status)
    {
        var request = new CoordinatorSetUserStatusRequest(status);
        var response = await SendRequestAsync<CoordinatorSetUserStatusResponse>(request);

        return (response.Result, response.User?.ToModel());
    }

    public async Task<(PushTokenResult Result, string? ReplyToken)> RegisterPushToken(string token,
        PushPlatform platform, PushKey key)
    {
        var request = new RegisterPushTokenRequest(token, platform, key.KeyId, Convert.ToBase64String(key.Key));
        var response = await SendRequestAsync<RegisterPushTokenResponse>(request);

        return (response.Result, response.ReplyToken);
    }

    public async Task<PushTokenResult> UnregisterPushToken(string token)
    {
        var request = new UnregisterPushTokenRequest(token);
        var response = await SendRequestAsync<UnregisterPushTokenResponse>(request);

        return response.Result;
    }

    public async Task<(SessionRequestResult Result, SessionModel? Session)> GetSessionInfo(int sessionId)
    {
        var request = new SessionInfoRequest(sessionId);
        var infoResponse = await SendRequestAsync<SessionInfoResponse>(request);
        
        return (infoResponse.Result, infoResponse.SessionsList
            .FirstOrDefault(s => s.Key == sessionId).Value?.ToModel());
    }

    public async Task<(SessionRequestResult Result, IDictionary<int, SessionModel>? Sessions, SessionLayout? Layout)>
        GetAllSessions()
    {
        var request = new SessionInfoRequest(null);
        var infoResponse = await SendRequestAsync<SessionInfoResponse>(request);
        
        return (infoResponse.Result, infoResponse.SessionsList?
            .ToDictionary(pair => pair.Key, pair => pair.Value.ToModel()), infoResponse.Layout?.ToModel());
    }
    
    public async Task<CreateSessionResult> CreateSession(
        NewSessionModel newSession)
    {
        var request = new CreateSessionRequest(newSession.ToDto());
        var createSessionResponse = await SendRequestAsync<CreateSessionResponse>(request);
        
        return createSessionResponse.Result;
    }

    public async Task<DeleteSessionResult> DeleteSession(
        int sessionId)
    {
        var request = new DeleteSessionRequest(sessionId);
        var deleteSessionResponse = await SendRequestAsync<DeleteSessionResponse>(request);
        
        return deleteSessionResponse.Result;
    }

    public async Task<(SessionLayoutResult Result, int? CategoryId)> CreateSessionCategory(string name)
    {
        var response = await SendRequestAsync<SessionLayoutResponse>(new CreateSessionCategoryRequest(name));

        return (response.Result, response.CategoryId);
    }

    public async Task<SessionLayoutResult> RenameSessionCategory(int categoryId, string name) =>
        (await SendRequestAsync<SessionLayoutResponse>(new RenameSessionCategoryRequest(categoryId, name))).Result;

    public async Task<SessionLayoutResult> DeleteSessionCategory(int categoryId) =>
        (await SendRequestAsync<SessionLayoutResponse>(new DeleteSessionCategoryRequest(categoryId))).Result;

    public async Task<SessionLayoutResult> MoveSession(int sessionId, int? categoryId, int index) =>
        (await SendRequestAsync<SessionLayoutResponse>(new MoveSessionRequest(sessionId, categoryId, index))).Result;

    public async Task<SessionLayoutResult> MoveSessionCategory(int categoryId, int index) =>
        (await SendRequestAsync<SessionLayoutResponse>(new MoveSessionCategoryRequest(categoryId, index))).Result;

    public async Task<(FetchMessagesResult Result, ChatHistoryPage? Page)> RequestChatHistoryPage(
        int sessionId,
        int? beforeMessageId,
        int take)
    {
        var request = new ChatSessionHistoryPageRequest(sessionId, beforeMessageId, take);

        var response = await SendRequestAsync<ChatSessionHistoryPageResponse>(request);

        return (response.Result, response.Messages == null
            ? null
            : new ChatHistoryPage(response.Messages.Select(message => message.ToModel()).ToList(),
                response.CurrentRevision));
    }

    public async Task<(FetchMessagesResult Result, ChatChanges? Changes)> RequestChatChanges(
        int sessionId,
        long afterRevision,
        int take)
    {
        var request = new ChatSessionChangesRequest(sessionId, afterRevision, take);

        var response = await SendRequestAsync<ChatSessionChangesResponse>(request);

        return (response.Result, response.Messages == null
            ? null
            : new ChatChanges(response.Messages.Select(message => message.ToModel()).ToList(),
                response.CurrentRevision, response.HasMore, response.Reset));
    }

    public async Task<(ChatReadResult Result, IReadOnlyList<ChatReadState>? States)> RequestChatReadStates()
    {
        var response = await SendRequestAsync<ChatReadStatesResponse>(new ChatReadStatesRequest());

        return (response.Result, response.States?.Select(state => state.ToModel()).ToList());
    }

    public async Task<(ChatReadResult Result, ChatReadState? State)> MarkChatRead(
        int sessionId,
        int messageId)
    {
        var response = await SendRequestAsync<ChatMarkReadResponse>(new ChatMarkReadRequest(sessionId, messageId));

        return (response.Result, response.State?.ToModel());
    }

    public async Task<(FetchMessageResult Result, ChatMessageModel? Message)> RequestChatMessage(
        int sessionId, 
        int messageId)
    {
        var request = new ChatSessionMessageRequest(sessionId, messageId);
        
        var chatSessionMessageResponse = await SendRequestAsync<ChatSessionMessageResponse>(request);
        
        return (chatSessionMessageResponse.Result, 
            chatSessionMessageResponse.Message?.ToModel());
    }

    public async Task<AddMessageResult> SendChatMessage(
        int sessionId, 
        NewChatMessage newMessage)
    {
        var request = new SendTextMessageRequest(sessionId, newMessage.ToDto());
        
        var sendTextMessageResponse = await SendRequestAsync<SendTextMessageResponse>(request);

        return sendTextMessageResponse.Result;
    }

    public async Task<DeleteMessageResult> DeleteChatMessage(
        int sessionId, 
        int messageId)
    {
        var request = new DeleteTextMessageRequest(sessionId, messageId);
        var deleteTextMessageResponse = await SendRequestAsync<DeleteTextMessageResponse>(request);

        return deleteTextMessageResponse.Result;
    }

    public async Task<EditMessageResult> EditChatMessage(
        int sessionId,
        int messageId,
        string text)
    {
        var request = new EditTextMessageRequest(sessionId, messageId, text);
        var editMessageResponse = await SendRequestAsync<EditMessageResponse>(request);

        return editMessageResponse.Result;
    }
    
    public async Task<LeaveSessionResult> LeaveSession(
        int sessionId)
    {
        var request = new LeaveSessionRequest(sessionId, SessionType.Hybrid);
        var leaveSessionResponse = await SendRequestAsync<LeaveSessionResponse>(request);
        
        return leaveSessionResponse.Result;
    }
    
    public async Task<LeaveSessionResult> KickFromHybridSession(
        int sessionId,
        int userId)
    {
        var request = new KickFromSessionRequest(sessionId, userId, SessionType.Hybrid, null);
        var kickResponse = await SendRequestAsync<KickFromSessionResponse>(request);

        return kickResponse.Result;
    }

    public async Task<ApproveUserSessionResult> ApproveInHybridSession(int sessionId, int userId)
    {
        var request = new ApproveUserRequest(sessionId, userId, SessionType.Hybrid);
        var response = await SendRequestAsync<ApproveUserResponse>(request);

        return response.Result;
    }

    public async Task<RejectUserSessionResult> RejectInHybridSession(int sessionId, int userId)
    {
        var request = new RejectUserRequest(sessionId, userId, SessionType.Hybrid, null);
        var response = await SendRequestAsync<RejectUserResponse>(request);

        return response.Result;
    }
    
    public async Task<ChangeSessionSettingsResult> ChangeHybridSessionSettings(int sessionId, string name,
        int capacity, bool isHostMustApprove, MediaSettings media)
    {
        var request = new ChangeHybridSessionSettingsRequest(sessionId, name, capacity, isHostMustApprove,
            media.ToDto());
        var response = await SendRequestAsync<ChangeSessionSettingsResponse>(request);

        return response.Result;
    }

    public async Task<ChangeSessionPermissionsResult> ChangeSessionPermissions(int sessionId,
        IReadOnlyList<PermissionOverride> overrides)
    {
        var request = new ChangeSessionPermissionsRequest(sessionId, overrides.ToDTO());
        var response = await SendRequestAsync<ChangeSessionPermissionsResponse>(request);

        return response.Result;
    }

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
            return Task.FromException<BaseMessage>(new RequestRejectedException(RequestRejectReason.InvalidRequest, details));
        }

        return _responseAwaiter.SendAndWaitAsync(request.RequestId, () =>
        {
            // Fails at once instead of waiting out the response timeout on a dead connection
            if (!_webSocketGateway.SendMessage(request))
                throw new ConnectionLostException("Not connected to the coordinator");
        });
    }
}
