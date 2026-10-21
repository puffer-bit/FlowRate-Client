using Client.Core.Settings;

namespace Client.Application.Ports.Repositories;

public interface IHotKeysSettingsRepository
{
    Task<HotKeysSettings> GetAsync();
    Task SaveAsync(HotKeysSettings settings);
}
