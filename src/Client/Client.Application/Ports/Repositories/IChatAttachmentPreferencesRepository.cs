using Client.Core.Chat;

namespace Client.Application.Ports.Repositories;

/// <summary>What is done to images and videos attached from now on: compressed, stripped of their metadata.</summary>
public interface IChatAttachmentPreferencesRepository
{
    Task<ChatAttachmentPreferences> GetAsync();
    Task SaveAsync(ChatAttachmentPreferences preferences);
}
