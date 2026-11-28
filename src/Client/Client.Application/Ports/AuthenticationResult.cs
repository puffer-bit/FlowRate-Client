using Shared.Enums.Auth;
using Client.Core.Users;

namespace Client.Application.Ports;

/// <param name="User">The authenticated user, set on success. A failed login throws a RequestFailedException.</param>
/// <param name="DeviceId">The device to log in with from now on, set on success.</param>
/// <param name="Challenge">What finishes the login, set with <see cref="AuthResult.SecondStageRequired"/>.</param>
public sealed record AuthenticationResult(AuthResult Result, UserModel? User, Guid? DeviceId,
    LoginChallenge? Challenge = null);
