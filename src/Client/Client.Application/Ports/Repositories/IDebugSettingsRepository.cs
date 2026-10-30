using Client.Core.Settings;

namespace Client.Application.Ports.Repositories;

public interface IDebugSettingsRepository
{
    Task<DebugSettings> GetAsync();
    Task SaveAsync(DebugSettings settings);
}
