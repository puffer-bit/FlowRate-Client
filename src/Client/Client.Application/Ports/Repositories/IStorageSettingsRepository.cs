using Client.Core.Settings;

namespace Client.Application.Ports.Repositories;

public interface IStorageSettingsRepository
{
    Task<StorageSettings> GetAsync();
    Task SaveAsync(StorageSettings settings);
}
