using Avalonia.Media;

namespace Client.Services;

/// <summary>
/// A picture picked for an avatar, made as small as an avatar is shown.
/// </summary>
/// <param name="Picture">What the dialog shows until the picture is saved; owned by the receiver.</param>
/// <param name="Encoded">What goes to the server.</param>
public sealed record PreparedAvatar(IImage Picture, byte[] Encoded, string MimeType);
