using Shared.Models.Requests.SessionActions.ChatSession.Models;
using Shared.Models.Requests.SessionActions.Generic.Models;
using Shared.Models.Requests.SessionActions.Generic.Models.SessionData;
using Client.Core.Chat;
using Client.Core.Sessions;

namespace Client.Api.Mappers;

internal static class RequestMapper
{
    // The server makes the sender the host, so NewSessionModel.HostId is not sent
    public static CreateSessionModel ToDto(this NewSessionModel model) => new(
        model.Name,
        model.Options.ToDto(),
        model.CategoryId);

    private static CreateSessionData ToDto(this NewSessionOptions options) => options switch
    {
        NewHybridSessionOptions hybrid => new HybridSessionCreateData(hybrid.EngineType, hybrid.Capacity, hybrid.Media?.ToDto()),

        NewChatSessionOptions chat => new ChatSessionCreateData(
            chat.Description,
            chat.IconPath,
            chat.IsAttachmentsAllowed,
            chat.IsVoiceMessageAllowed,
            chat.IsVideoMessagesAllowed,
            chat.IsDelayedMessagesAllowed, chat.Kind, chat.MemberIds),

        _ => throw new ArgumentOutOfRangeException(nameof(options), options.GetType().Name, "Unknown session type")
    };

    // The author, sent time and state flags are set by the server and are not sent
    public static NewChatMessageModel ToDto(this NewChatMessage model) => new()
    {
        ChatId = model.ChatId,
        ScheduledAt = model.DelayedSentTime,
        ExpiresAt = model.DisposeTime,

        TextContent = model.TextContent == null ? null : ChatMessageMapper.MapTextContentDto(model.TextContent),
        ReplyToMessageId = model.ReplyContent?.MessageId,

        Files = model.Images.Select(image => (NewChatFileModel)new NewChatImageModel
        {
            UploadId = image.UploadId,
            PreviewUploadId = image.PreviewUploadId,
            FileName = image.FileName,
            Description = image.Description,
            Width = image.Width,
            Height = image.Height
        }).Concat(model.Files.Select(file => new NewChatFileModel
        {
            UploadId = file.UploadId,
            FileName = file.FileName,
            PreviewUploadId = file.PreviewUploadId
        })).ToList(),

        IsLinkPreviewDisabled = model.IsLinkPreviewDisabled
    };
}
