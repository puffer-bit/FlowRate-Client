using Client.Core.Settings;


namespace Client.Application.Settings;

public interface IBehaviorService
{
    /// <summary>Raised after preferences have been saved successfully; may run on any thread.</summary>
    event Action<BehaviorSettings>? SettingsChanged;

    /// <summary>
    /// The saved server and account the application connects to when it starts, as <see cref="AutoConnectChoice"/>
    /// resolves them; null when nothing is saved to connect to.
    /// </summary>
    Task<(SavedCoordinatorModel Coordinator, SavedUserModel User)?> GetAutoConnectInstance();

    /// <summary>What the user picked for the startup connection.</summary>
    Task<AutoConnectChoice> GetAutoConnectChoiceAsync();
    Task SetAutoConnectChoiceAsync(AutoConnectChoice choice);

    /// <summary>Records a successful connection of a saved account, which the "last" choices follow.</summary>
    Task RecordConnectionAsync(string coordinatorId, int userId);

    Task<BehaviorSettings> GetSettingsAsync();
    Task SaveSettingsAsync(BehaviorSettings settings);
}
