using System.Reflection;
using Client.Application.Ports;
using Client.Application.Ports.Repositories;
using Client.Core.Chat;
using Client.Core.Coordinator;
using Client.Core.Push;
using Client.Core.Users;
using Shared.Enums;
using Shared.Enums.Auth;
using Shared.Enums.User;

namespace Client.Application.Tests.Fakes;

/// <summary>
/// A coordinator that accepts every login. Only what a connection needs is answered; any other
/// request of the wide gateway interface fails the test that makes it.
/// </summary>
public class FakeGateway : DispatchProxy
{
    public const string CoordinatorId = "coordinator";

    public int Connects { get; private set; }
    public int Disconnects { get; private set; }
    public List<(string Token, PushPlatform Platform, PushKey Key)> Registered { get; } = [];
    public List<string> Unregistered { get; } = [];
    /// <summary>What the server fails the push requests with; null accepts them.</summary>
    public ErrorCode? PushError { get; set; }
    public string ReplyToken { get; set; } = "reply-token";

    /// <summary>The uploads the server holds, by the id it gave them.</summary>
    public Dictionary<string, (MediaUpload Upload, byte[] Content)> Uploads { get; } = [];
    public MediaTransferResult UploadResult { get; set; } = MediaTransferResult.NoError;

    /// <summary>The avatar requests: the upload, null for a reset, and the user named.</summary>
    public List<(string? UploadId, int? UserId)> AvatarRequests { get; } = [];
    public ErrorCode? AvatarError { get; set; }

    /// <summary>The roles the server answers the login with, lowest first.</summary>
    public IReadOnlyList<RoleModel> Roles { get; set; } =
    [
        new(1, "@everyone", null, 0, Permission.CreateSessions, true, 0),
        new(2, "Admin", null, 1, Permission.Administrator, false, 0)
    ];

    /// <summary>What the server fails the login with; null lets it go on.</summary>
    public ErrorCode? LoginError { get; set; }

    /// <summary>The users banned, frozen, reinstated or deleted, with the state asked; null for a deletion.</summary>
    public List<(int UserId, UserState? State)> ModerationRequests { get; } = [];

    /// <summary>What the server asks for after the identity; null lets the user in at once.</summary>
    public LoginChallenge? Challenge { get; set; }

    /// <summary>The account password the second login stage takes.</summary>
    public string? AccountPassword { get; set; }

    /// <summary>The one-time code the second login stage takes.</summary>
    public string? TwoFactorCode { get; set; }

    /// <summary>The answers to the second login stage, as they came.</summary>
    public List<(string? Password, string? Code)> SecondStageAnswers { get; } = [];

    /// <summary>The identity and session token of every login.</summary>
    public List<(string? Identity, string? SessionToken)> SentLogins { get; } = [];

    /// <summary>The token of the session the server knows: the last one it gave out, or none.</summary>
    public string? SessionToken { get; set; }

    private int _issuedTokens;

    /// <summary>The roles of the user who logs in.</summary>
    public IReadOnlyList<int> UserRoleIds { get; set; } = [];

    /// <summary>Hands the session an event as the server would send it.</summary>
    public void Raise(Client.Core.Coordinator.CoordinatorEvent coordinatorEvent) => _eventReceived?.Invoke(coordinatorEvent);

    /// <summary>Whether the socket is open, as the gateway under test would see it.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>False makes every connect fail, as an unreachable server does.</summary>
    public bool IsReachable { get; set; } = true;

    /// <summary>False leaves the checks of the connection without an answer.</summary>
    public bool IsAnswering { get; set; } = true;

    public int Checks { get; private set; }

    /// <summary>How long closing the socket takes, as a server slow to answer the close does.</summary>
    public TimeSpan DisconnectDuration { get; set; }

    /// <summary>
    /// How many times a connect or a disconnect started while another was still running: the real
    /// gateway holds one socket, and two of them at once close each other's.
    /// </summary>
    public int OverlappingCalls => Volatile.Read(ref _overlappingCalls);

    private Action<string>? _connectionLost;
    private Action<Client.Core.Coordinator.CoordinatorEvent>? _eventReceived;
    private int _runningCalls;
    private int _overlappingCalls;

    public static (ICoordinatorGateway Gateway, FakeGateway Fake) Create()
    {
        var gateway = Create<ICoordinatorGateway, FakeGateway>();
        return (gateway, (FakeGateway)(object)gateway);
    }

    public void LoseConnection()
    {
        IsOpen = false;
        _connectionLost?.Invoke("test");
    }

    private async Task<TransportConnectResult> ConnectAsync()
    {
        Enter();
        try
        {
            await Task.Yield();
            Connects++;
            IsOpen = IsReachable;
            return new TransportConnectResult(IsReachable, IsReachable ? null : "unreachable");
        }
        finally
        {
            Interlocked.Decrement(ref _runningCalls);
        }
    }

    private async Task DisconnectAsync()
    {
        Enter();
        try
        {
            Disconnects++;
            IsOpen = false;
            if (DisconnectDuration > TimeSpan.Zero)
                await Task.Delay(DisconnectDuration);
        }
        finally
        {
            Interlocked.Decrement(ref _runningCalls);
        }
    }

    private async Task<MediaUploadOutcome> UploadAsync(MediaUpload upload)
    {
        if (UploadResult != MediaTransferResult.NoError)
            return new MediaUploadOutcome(UploadResult, null);

        await using var content = await upload.OpenContent();
        using var received = new MemoryStream();
        await content.CopyToAsync(received);

        var id = $"{Uploads.Count + 1:x32}";
        Uploads[id] = (upload, received.ToArray());
        return new MediaUploadOutcome(MediaTransferResult.NoError, id);
    }

    private UserModel LoggedInUser() =>
        new() { Id = 1, CoordinatorInstanceId = CoordinatorId, Username = "alice", RoleIds = UserRoleIds };

    private AuthenticationResult Authenticate(string? userIdentity, string? sessionToken)
    {
        SentLogins.Add((userIdentity, sessionToken));

        if (LoginError is { } loginError)
            throw new RequestFailedException(loginError, null);
        // The token of the session lets the device in without the second stage, and is not told the identity
        if (sessionToken != null && sessionToken == SessionToken)
            return new AuthenticationResult(AuthResult.Success, LoggedInUser(), null, SessionToken: IssueToken());
        if (Challenge != null)
            return new AuthenticationResult(AuthResult.SecondStageRequired, null, null, Challenge);
        return new AuthenticationResult(AuthResult.Success, LoggedInUser(), "#identity", SessionToken: IssueToken());
    }

    private string IssueToken() => SessionToken = $"token-{++_issuedTokens}";

    private AuthenticationResult FinishLogin(string? password, string? code)
    {
        SecondStageAnswers.Add((password, code));

        var isProven = Challenge != null
                       && (!Challenge.AsksPassword || password == AccountPassword)
                       && (!Challenge.AsksTwoFactorCode || code == TwoFactorCode);

        return isProven
            ? new AuthenticationResult(AuthResult.Success, LoggedInUser(), null, SessionToken: IssueToken())
            : throw new RequestFailedException(ErrorCode.AuthFailed, null);
    }

    private static Task<T> Fail<T>(ErrorCode code) => Task.FromException<T>(new RequestFailedException(code, null));

    private void Enter()
    {
        if (Interlocked.Increment(ref _runningCalls) > 1)
            Interlocked.Increment(ref _overlappingCalls);
    }

    private async Task<bool> CheckAsync(TimeSpan timeout)
    {
        Checks++;
        if (IsAnswering)
            return true;

        await Task.Delay(timeout);
        return false;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        switch (targetMethod!.Name)
        {
            case "add_ConnectionLost":
                _connectionLost += (Action<string>)args![0]!;
                return null;
            case "remove_ConnectionLost":
                _connectionLost -= (Action<string>)args![0]!;
                return null;
            case "add_EventReceived":
                _eventReceived += (Action<Client.Core.Coordinator.CoordinatorEvent>)args![0]!;
                return null;
            case "remove_EventReceived":
                _eventReceived -= (Action<Client.Core.Coordinator.CoordinatorEvent>)args![0]!;
                return null;
            case nameof(IDisposable.Dispose):
                return null;
            case nameof(ICoordinatorGateway.GetRoles):
                return Task.FromResult(Roles);
            case nameof(ICoordinatorGateway.ConnectWebSocketAsync):
                return ConnectAsync();
            case nameof(ICoordinatorGateway.DisconnectWebSocketAsync):
                return DisconnectAsync();
            case nameof(ICoordinatorGateway.CheckConnectionAsync):
                return CheckAsync((TimeSpan)args![0]!);
            case nameof(ICoordinatorGateway.AuthenticateAsync):
                try
                {
                    return Task.FromResult(Authenticate((string?)args![1], (string?)args[2]));
                }
                catch (RequestFailedException exception)
                {
                    return Task.FromException<AuthenticationResult>(exception);
                }
            case nameof(ICoordinatorGateway.FinishLoginAsync):
                try
                {
                    return Task.FromResult(FinishLogin((string?)args![0], (string?)args[1]));
                }
                catch (RequestFailedException exception)
                {
                    return Task.FromException<AuthenticationResult>(exception);
                }
            case nameof(ICoordinatorGateway.SetAccountPassword):
                AccountPassword = (string?)args![1];
                Challenge = AccountPassword == null ? null : new LoginChallenge(true, false, false);
                return Task.FromResult(new AccountSecurity(AccountPassword != null, false, false, false, []));
            case nameof(ICoordinatorGateway.GetCoordinatorInfo):
                return Task.FromResult<CoordinatorSessionModel?>(new CoordinatorSessionModel { Id = CoordinatorId, Name = "Test" });
            case nameof(ICoordinatorGateway.RegisterPushToken):
                Registered.Add(((string)args![0]!, (PushPlatform)args[1]!, (PushKey)args[2]!));
                return PushError is { } pushError ? Fail<string?>(pushError) : Task.FromResult<string?>(ReplyToken);
            case nameof(ICoordinatorGateway.UploadMedia):
                return UploadAsync((MediaUpload)args![0]!);
            case nameof(ICoordinatorGateway.SetAvatar):
                AvatarRequests.Add(((string?)args![0], (int?)args[1]));
                return AvatarError is { } avatarError
                    ? Fail<UserModel>(avatarError)
                    : Task.FromResult(new UserModel
                    {
                        Id = 1,
                        CoordinatorInstanceId = CoordinatorId,
                        Username = "alice",
                        AvatarUrl = args[0] == null ? null : $"/media/avatars/{args[0]}"
                    });
            case nameof(ICoordinatorGateway.SetUserState):
                ModerationRequests.Add(((int)args![0]!, (UserState)args[1]!));
                return Task.FromResult(new UserModel { Id = (int)args[0]!, CoordinatorInstanceId = CoordinatorId, Username = "bob" });
            case nameof(ICoordinatorGateway.DeleteUser):
                ModerationRequests.Add(((int)args![0]!, null));
                return Task.CompletedTask;
            case nameof(ICoordinatorGateway.UnregisterPushToken):
                Unregistered.Add((string)args![0]!);
                return PushError is { } unregisterError ? Fail<object>(unregisterError) : Task.CompletedTask;
            default:
                throw new NotSupportedException(targetMethod.Name);
        }
    }
}

public sealed class NoTrustedCertificates : ITrustedCertificatesRepository
{
    public Task<ServerCertificateModel?> GetAsync(string host, int port) => Task.FromResult<ServerCertificateModel?>(null);
    public Task<IReadOnlyList<ServerCertificateModel>> GetAllAsync() => Task.FromResult<IReadOnlyList<ServerCertificateModel>>([]);
    public Task SaveAsync(ServerCertificateModel certificate) => Task.CompletedTask;
    public Task BindCoordinatorAsync(string host, int port, string coordinatorId) => Task.CompletedTask;
    public Task RemoveAsync(string host, int port) => Task.CompletedTask;
}

public sealed class MemoryPushKeys : IPushKeysRepository
{
    public Dictionary<string, PushKey> Keys { get; } = [];

    public Task<PushKey?> GetByCoordinatorAsync(string coordinatorInstanceId) =>
        Task.FromResult(Keys.GetValueOrDefault(coordinatorInstanceId));

    public Task<PushKey?> GetByKeyIdAsync(string keyId) =>
        Task.FromResult(Keys.Values.FirstOrDefault(key => key.KeyId == keyId));

    public Task SaveAsync(PushKey key)
    {
        Keys[key.CoordinatorInstanceId] = key;
        return Task.CompletedTask;
    }

    public Dictionary<string, PushReplyTarget> ReplyTargets { get; } = [];

    public Task SetReplyTargetAsync(string coordinatorInstanceId, PushReplyTarget target)
    {
        if (Keys.ContainsKey(coordinatorInstanceId))
            ReplyTargets[coordinatorInstanceId] = target;
        return Task.CompletedTask;
    }

    public Task<PushReplyTarget?> GetReplyTargetAsync(string coordinatorInstanceId) =>
        Task.FromResult(ReplyTargets.GetValueOrDefault(coordinatorInstanceId));

    public Task RemoveAsync(string coordinatorInstanceId)
    {
        Keys.Remove(coordinatorInstanceId);
        ReplyTargets.Remove(coordinatorInstanceId);
        return Task.CompletedTask;
    }
}

public sealed class FixedPushToken(string? token) : IPushTokenProvider
{
    public string? Token { get; set; } = token;
    public PushPlatform Platform => PushPlatform.Android;
    public event Action<string>? TokenChanged;

    public Task<string?> GetTokenAsync() => Task.FromResult(Token);

    public void Replace(string token)
    {
        Token = token;
        TokenChanged?.Invoke(token);
    }
}
