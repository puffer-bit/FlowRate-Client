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
            SentTime = dto.SentTime,
            DelayedSentTime = dto.DelayedSentTime,
            DisposeTime = dto.DisposeTime,
            EditTime = dto.EditTime,

            TextContent = MapTextContent(dto.TextContent),
            ReplyContent = MapReplyContent(dto.ReplyContent),

            Images = dto.Images?.Select(MapImageContent).ToList() ?? new List<ChatImageContent>(),
            Video = dto.Video?.Select(MapVideoContent).ToList() ?? new List<ChatVideoContent>(),
            Files = dto.Files?.Select(MapFileContent).ToList() ?? new List<ChatFileContent>(),

            Reactions = dto.Reactions?.Select(MapReaction).ToList() ?? new List<ChatReaction>(),
            UsersIds = dto.UsersIds?.ToList() ?? new List<string>(),
            MentionedUserIds = dto.MentionedUserIds?.ToList() ?? [],
            MentionsEveryone = dto.MentionsEveryone,
            MentionsHere = dto.MentionsHere,

            IsDelayed = dto.IsDelayed,
            IsEdited = dto.IsEdited,
            IsReposted = dto.IsReposted,
            IsDelivered = dto.IsDelivered,
            IsDeleted = dto.IsDeleted,
            IsReadOnly = dto.IsReadOnly,
            IsSystem = dto.IsSystem,
            IsDisposable = dto.IsDisposable,
        };
    }

    private static ChatTextContent? MapTextContent(ChatTextContentDTO? dto)
        => dto == null ? null : new ChatTextContent { Text = dto.Text };
    
    private static ChatReplyContent? MapReplyContent(ChatReplyContentDTO? dto)
    {
        if (dto == null)
            return null;

        return new ChatReplyContent
        {
            MessageId = dto.MessageId,
            IsDeleted = dto.IsDeleted,
            SenderUserId = dto.SenderUserId,
            SentTime = dto.SentTime,
            TextContent = MapTextContent(dto.TextContent),
        };
    }

    private static ChatImageContent MapImageContent(ChatImageContentDTO dto)
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

    private static ChatVideoContent MapVideoContent(ChatVideoContentDTO dto)
        => new ChatVideoContent
        {
            Id = dto.Id,
            FileName = dto.FileName,
            Thumbnail = dto.Thumbnail,
            Description = dto.Description,
            Size = dto.Size,
            MimeType = dto.MimeType,
            Width = dto.Width,
            Height = dto.Height,
            Duration = dto.Duration
        };

    private static ChatFileContent MapFileContent(ChatFileContentDTO dto)
        => new ChatFileContent
        {
            Id = dto.Id,
            FileName = dto.FileName,
            Size = dto.Size,
            MimeType = dto.MimeType
        };

    private static ChatReaction MapReaction(ChatReactionDTO dto)
        => new ChatReaction { UserId = dto.UserId, Emoji = dto.Emoji };

    // DTO mappers
    internal static ChatTextContentDTO MapTextContentDto(ChatTextContent model) => new() { Text = model.Text };

    internal static ChatVideoContentDTO MapVideoContentDto(ChatVideoContent model)
        => new ChatVideoContentDTO
        {
            Id = model.Id,
            FileName = model.FileName,
            Thumbnail = model.Thumbnail,
            Description = model.Description,
            Size = model.Size,
            MimeType = model.MimeType,
            Width = model.Width,
            Height = model.Height,
            Duration = model.Duration
        };
}
