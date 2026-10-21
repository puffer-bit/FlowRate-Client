using Shared.Models.Media;

namespace Client.Core.Users;

/// <summary>
/// The address of an avatar as a coordinator gives it in <see cref="UserModel.AvatarUrl"/>.
/// </summary>
public static class AvatarAddress
{
    private const int IdMaxLength = 64;

    /// <summary>
    /// The id the avatar is fetched and kept by, or null when the user has no avatar. An address
    /// of another form is not followed: the server could name anything in it.
    /// </summary>
    public static string? IdOf(string? avatarUrl)
    {
        const string prefix = MediaEndpoints.Avatars + "/";
        if (avatarUrl == null || !avatarUrl.StartsWith(prefix, StringComparison.Ordinal))
            return null;

        var id = avatarUrl[prefix.Length..];
        return id.Length is > 0 and <= IdMaxLength && id.All(char.IsAsciiLetterOrDigit) ? id : null;
    }
}
