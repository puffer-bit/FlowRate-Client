using ReactiveUI;

namespace Client.ViewModels.ServerSettings;

/// <summary>
/// The SFU nodes of the server, the ones the voice sessions of this coordinator go through checked.
/// </summary>
public class SfuNodesTabVM : ReactiveObject
{
    public CoordinatorSettingsFormVM Form { get; }

    public SfuNodesTabVM(CoordinatorSettingsFormVM form)
    {
        Form = form;
    }
}
