using Shared.Validation;

namespace Client.Services;

/// <summary>Why a picked file cannot be an avatar.</summary>
public enum AvatarRejection
{
    None,
    NotAnImage,

    /// <summary>
    /// An animation goes to the server as it is, since it cannot be encoded again, and the server
    /// takes no more than <see cref="Limits.AvatarMaxSize"/>.
    /// </summary>
    AnimationTooLarge
}
