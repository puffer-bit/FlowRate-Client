using Shared.Models.DataTransferObjects.ChatSession.Messages;
using Shared.Models.DataTransferObjects.ChatSession.Messages.Content;
using Shared.Models.DataTransferObjects.ChatSession.Messages.Reactions;
using Client.Core.Chat;

namespace Client.Api.Mappers;

internal static class ChatMessageMapper
{
    /// <summary>
    /// <see cref="ChatMessageModel.IsMine"/> is left unset: only the caller knows who the current user is.
    /// </summary>
    public static ChatMessageModel ToModel(this ChatMessageDTO dto)
    {
        return new ChatMessageModel
        {
            Id = dto.Id,
            ChatId = dto.ChatId,
            Revision = dto.Revision,
            SenderUserId = dto.SenderUserId,
            SentTime = dto.SentAt,
            DelayedSentTime = dto.ScheduledAt,
            DisposeTime = dto.ExpiresAt,
            EditTime = dto.EditedAt,
            PinnedAt = dto.PinnedAt,
            PinnedByUserId = dto.PinnedByUserId,

            TextContent = MapTextContent(dto.TextContent),
            ReplyContent = MapReplyContent(dto.ReplyContent),

            // The chat shows images in a grid apart from other files
            Images = dto.Files?.OfType<ChatImageFileDTO>().Select(MapImageContent).ToList() ?? new List<ChatImageContent>(),
            Files = dto.Files?.Where(file => file is not ChatImageFileDTO).Select(MapFileContent).ToList()
                    ?? new List<ChatFileContent>(),
            LinkPreview = dto.LinkPreview?.ToModel(),
            IsLinkPreviewDisabled = dto.IsLinkPreviewDisabled,

            Reactions = dto.Reactions?.Select(MapReaction).ToList() ?? new List<ChatReaction>(),
            MentionedUserIds = dto.MentionedUserIds?.ToList() ?? [],
            MentionsEveryone = dto.MentionsEveryone,
            MentionsHere = dto.MentionsHere,

            IsDelayed = dto.ScheduledAt != null,
            IsEdited = dto.EditedAt != null,
            IsDeleted = dto.IsDeleted,
            IsReadOnly = dto.IsReadOnly,
            IsSystem = dto.IsSystem,
            SystemContent = dto.SystemContent,
            IsDisposable = dto.ExpiresAt != null,
        };
    }

    public static ChatLinkPreview ToModel(this ChatLinkPreviewDTO dto) => new()
    {
        Id = dto.Id,
        Url = dto.Url,
        SiteName = dto.SiteName,
        Title = dto.Title,
        Description = dto.Description,
        HasImage = dto.HasImage,
        ImageWidth = dto.ImageWidth,
        ImageHeight = dto.ImageHeight,
        IsLargeImage = dto.IsLargeImage
    };

    private static ChatTextContent? MapTextContent(ChatTextContentDTO? dto)
        => dto == null ? null : new ChatTextContent { Text = dto.Text, CommandResult = dto.CommandResult };
    
    private static ChatReplyContent? MapReplyContent(ChatReplyContentDTO? dto)
    {
        if (dto == null)
            return null;

        return new ChatReplyContent
        {
            MessageId = dto.MessageId,
            HasAttachments = dto.HasAttachments,
            IsDeleted = dto.IsDeleted,
            SenderUserId = dto.SenderUserId,
            SentTime = dto.SentAt,
            TextContent = MapTextContent(dto.TextContent),
        };
    }

    private static ChatImageContent MapImageContent(ChatImageFileDTO dto)
        => new ChatImageContent
        {
            Id = dto.Id,
            FileName = dto.FileName,
            Thumbnail = dto.Thumbnail,
            Description = dto.Description,
            Size = dto.Size,
            MimeType = dto.MimeType,
            Width = dto.Width,
            Height = dto.Height
        };

    private static ChatFileContent MapFileContent(ChatFileDTO dto)
        => new ChatFileContent
        {
            Id = dto.Id,
            FileName = dto.FileName,
            Size = dto.Size,
            MimeType = dto.MimeType,
            Video = dto is ChatVideoFileDTO video
                ? new ChatFileVideo(Math.Max(0, video.Width), Math.Max(0, video.Height),
                    video.Duration is { } duration && duration > TimeSpan.Zero ? duration : null, video.HasPreview)
                : null
        };

    private static ChatReaction MapReaction(ChatReactionDTO dto)
        => new ChatReaction { UserId = dto.UserId, Emoji = dto.Emoji, ReactedAt = dto.ReactedAt };


    // DTO mappers
    internal static ChatTextContentDTO MapTextContentDto(ChatTextContent model) => new() { Text = model.Text };
}
