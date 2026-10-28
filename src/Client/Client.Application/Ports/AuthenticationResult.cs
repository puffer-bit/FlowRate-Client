using Shared.Enums.Auth;
using Client.Core.Users;

namespace Client.Application.Ports;

/// <param name="User">The authenticated user, set on success. A failed login throws a RequestFailedException.</param>
/// <param name="UserIdentity">Identity to log in with next time, set on success of the first stage.</param>
/// <param name="Challenge">What finishes the login, set with <see cref="AuthResult.SecondStageRequired"/>.</param>
public sealed record AuthenticationResult(AuthResult Result, UserModel? User, string? UserIdentity,
    LoginChallenge? Challenge = null);
