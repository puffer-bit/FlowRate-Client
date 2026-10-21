using Client.Application.Ports.Repositories;
using Client.Application.Settings;
using Client.Core.Settings;
using Xunit;

namespace Client.Application.Tests;

public class HotKeysServiceTests
{
    private readonly MemoryHotKeys _repository = new();
    private readonly HotKeysService _service;
    private readonly List<HotKeyAction> _triggered = [];

    public HotKeysServiceTests()
    {
        _service = new HotKeysService(_repository);
        _service.Triggered += _triggered.Add;
    }

    private static HotKeysSettings Assigned(bool isGlobal = true) => new HotKeysSettings()
        .With(new HotKeyBinding(HotKeyAction.ToggleMicrophone, "M", isGlobal));

    [Fact]
    public async Task SavedSettings_TakeEffectAtStart()
    {
        _repository.Value = Assigned();
        var changes = 0;
        _service.SettingsChanged += () => changes++;

        await _service.ApplyAsync();

        Assert.Equal("M", _service.Current.Get(HotKeyAction.ToggleMicrophone).Gesture);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task Save_TakesEffectAtOnce()
    {
        var changes = 0;
        _service.SettingsChanged += () => changes++;

        await _service.SaveSettingsAsync(Assigned());

        Assert.Equal(1, changes);
        Assert.Same(_repository.Value, _service.Current);
        _service.Raise(HotKeyAction.ToggleMicrophone);
        Assert.Equal([HotKeyAction.ToggleMicrophone], _triggered);
    }

    [Fact]
    public async Task Raise_DoesNothing_WhenTurnedOffSuspendedOrNotAssigned()
    {
        await _service.SaveSettingsAsync(Assigned() with { IsEnabled = false });
        _service.Raise(HotKeyAction.ToggleMicrophone);

        await _service.SaveSettingsAsync(Assigned());
        _service.IsSuspended = true;
        _service.Raise(HotKeyAction.ToggleMicrophone);

        _service.IsSuspended = false;
        _service.Raise(HotKeyAction.ToggleSound);

        Assert.Empty(_triggered);
    }

    [Fact]
    public async Task IsHeardGlobally_NeedsBackendAndBothSwitches()
    {
        await _service.SaveSettingsAsync(Assigned());
        Assert.False(_service.IsHeardGlobally(HotKeyAction.ToggleMicrophone));

        _service.Backend = GlobalHotKeysBackend.XInput;
        Assert.True(_service.IsHeardGlobally(HotKeyAction.ToggleMicrophone));

        await _service.SaveSettingsAsync(Assigned() with { IsGlobalEnabled = false });
        Assert.False(_service.IsHeardGlobally(HotKeyAction.ToggleMicrophone));

        await _service.SaveSettingsAsync(Assigned(isGlobal: false));
        Assert.False(_service.IsHeardGlobally(HotKeyAction.ToggleMicrophone));
    }

    [Fact]
    public void Backend_RaisesSettingsChanged_OnlyWhenChanged()
    {
        var changes = 0;
        _service.SettingsChanged += () => changes++;

        _service.Backend = GlobalHotKeysBackend.Evdev;
        _service.Backend = GlobalHotKeysBackend.Evdev;

        Assert.Equal(1, changes);
    }

    private sealed class MemoryHotKeys : IHotKeysSettingsRepository
    {
        public HotKeysSettings Value { get; set; } = new();

        public Task<HotKeysSettings> GetAsync() => Task.FromResult(Value);

        public Task SaveAsync(HotKeysSettings settings)
        {
            Value = settings;
            return Task.CompletedTask;
        }
    }
}
