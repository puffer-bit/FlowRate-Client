using Shared.Enums.Auth;
using Client.Core.Users;

namespace Client.Application.Ports;

/// <param name="User">The authenticated user, set on success.</param>
/// <param name="UserIdentity">Identity to log in with next time, set on success.</param>
public sealed record AuthenticationResult(AuthResult Result, UserModel? User, string? UserIdentity);
