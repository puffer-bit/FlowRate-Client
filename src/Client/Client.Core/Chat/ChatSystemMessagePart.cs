namespace Client.Core.Chat;

/// <summary>A plain event fragment, optionally linked to a resolved user profile.</summary>
public sealed record ChatSystemMessagePart(string Text, int? UserId = null);
