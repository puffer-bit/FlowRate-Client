using Client.Application.Ports.Repositories;
using Client.Core.Settings;

namespace Client.Application.Settings;

public class HotKeysService : IHotKeysService
{
    private readonly IHotKeysSettingsRepository _repository;

    public HotKeysService(IHotKeysSettingsRepository repository)
    {
        _repository = repository;
    }

    public HotKeysSettings Current { get; private set; } = new();

    public GlobalHotKeysBackend Backend
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            SettingsChanged?.Invoke();
        }
    }

    public bool IsSuspended { get; set; }

    public event Action? SettingsChanged;
    public event Action<HotKeyAction>? Triggered;

    public async Task ApplyAsync()
    {
        Current = await _repository.GetAsync();
        SettingsChanged?.Invoke();
    }

    public async Task SaveSettingsAsync(HotKeysSettings settings)
    {
        Current = settings;
        SettingsChanged?.Invoke();

        await _repository.SaveAsync(settings);
    }

    public bool IsHeardGlobally(HotKeyAction action) =>
        Backend != GlobalHotKeysBackend.None
        && Current is { IsEnabled: true, IsGlobalEnabled: true }
        && Current.Get(action).IsGlobal;

    public void Raise(HotKeyAction action)
    {
        if (IsSuspended || !Current.IsEnabled || Current.Get(action).Gesture == null)
            return;

        Triggered?.Invoke(action);
    }
}
