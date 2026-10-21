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
        model.Capacity,
        model.Options.ToDto(),
        model.CategoryId);

    private static CreateSessionData ToDto(this NewSessionOptions options) => options switch
    {
        NewHybridSessionOptions hybrid => new HybridSessionCreateData(
            hybrid.EngineType,
            hybrid.VideoCodecs,
            hybrid.AudioCodecs,
            hybrid.Resolution,
            hybrid.VideoBitrate,
            hybrid.IsAudioTransferEnabled,
            hybrid.IsDataChannelEnabled,
            hybrid.IsSimulcastEnabled,
            hybrid.IsTunnelingEnabled,
            hybrid.IsProxyActive,
            hybrid.DisableStun,
            hybrid.DisableTurn),

        NewChatSessionOptions chat => new ChatSessionCreateData(
            chat.Description,
            chat.IconPath,
            chat.IsEncrypted,
            chat.IsAttachmentsAllowed,
            chat.IsVoiceMessageAllowed,
            chat.IsVideoMessagesAllowed,
            chat.IsDelayedMessagesAllowed),

        _ => throw new ArgumentOutOfRangeException(nameof(options), options.GetType().Name, "Unknown session type")
    };

    // The author, sent time and state flags are set by the server and are not sent
    public static NewChatMessageModel ToDto(this NewChatMessage model) => new()
    {
        ChatId = model.ChatId,
        DelayedSentTime = model.DelayedSentTime,
        DisposeTime = model.DisposeTime,

        TextContent = model.TextContent == null ? null : ChatMessageMapper.MapTextContentDto(model.TextContent),
        ReplyToMessageId = model.ReplyContent?.MessageId,

        Images = model.Images.Select(image => new NewChatImageModel
        {
            UploadId = image.UploadId,
            PreviewUploadId = image.PreviewUploadId,
            FileName = image.FileName,
            Description = image.Description,
            Width = image.Width,
            Height = image.Height
        }).ToList(),
        Video = model.Video.Select(ChatMessageMapper.MapVideoContentDto).ToList(),
        Files = model.Files.Select(ChatMessageMapper.MapFileContentDto).ToList(),

        IsDelayed = model.IsDelayed,
        IsDisposable = model.IsDisposable
    };
}
