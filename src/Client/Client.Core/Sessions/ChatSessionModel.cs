namespace Client.Core.Sessions;

public class ChatSessionModel : SessionModel
{
    public Shared.Enums.ChatKind Kind { get; init; }
    public int[] MemberIds { get; init; } = [];
    public Client.Core.Chat.ChatMessageModel? LastMessage { get; init; }
    public bool IsPrivate => Kind != Shared.Enums.ChatKind.ServerChannel;

    public required int CreatorUserId { get; init; }
    public int? AdminUserId { get; init; }
    public string? Description { get; init; }
    public string? IconPath { get; init; }

    public bool IsReadOnly { get; init; }
    public bool IsArchived { get; init; }
    public bool IsAttachmentsAllowed { get; init; }
    public bool IsVoiceMessagesAllowed { get; init; }
    public bool IsVideoMessagesAllowed { get; init; }
    public bool IsDelayedMessagesAllowed { get; init; }
}