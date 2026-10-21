using Shared.Models.DataTransferObjects.ChatSession;
using Client.Core.Chat;

namespace Client.Api.Mappers;

internal static class ChatReadStateMapper
{
    public static ChatReadState ToModel(this ChatReadStateDTO dto) =>
        new(dto.ChatId, dto.LastReadMessageId, dto.UnreadCount, dto.MentionCount);
}
