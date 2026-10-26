using Client.Core.Users;

namespace Client.Application.Coordinator;

/// <summary>
/// Asks the user for what finishes a login: the account password, the one-time code or both.
/// </summary>
/// <param name="cancellationToken">Canceled when the login is given up from elsewhere; the question goes away then.</param>
/// <returns>The answer, or null when the user gave the login up.</returns>
public delegate Task<LoginAnswer?> LoginPromptHandler(LoginPrompt prompt, CancellationToken cancellationToken);

/// <param name="AsksPassword">The password is needed and not known here, or the known one was refused.</param>
/// <param name="AsksTwoFactorCode">A one-time code is needed.</param>
/// <param name="IsRetry">The previous answer was refused.</param>
/// <param name="IsPasswordRemembered">The password is kept with the saved identity; the remember box starts checked.</param>
public sealed record LoginPrompt(
    string CoordinatorAddress,
    LoginChallenge Challenge,
    bool AsksPassword,
    bool AsksTwoFactorCode,
    bool IsRetry,
    bool IsPasswordRemembered);

/// <param name="RememberPassword">Keep the password with the saved identity, so that the next start does not ask.</param>
public sealed record LoginAnswer(string? Password, string? TwoFactorCode, bool RememberPassword);
