using ReactiveUI;

namespace Client.ViewModels.ServerSettings;

/// <summary>
/// The limits of the files users send.
/// </summary>
public class ServerStorageTabVM : ReactiveObject
{
    public CoordinatorSettingsFormVM Form { get; }

    public ServerStorageTabVM(CoordinatorSettingsFormVM form)
    {
        Form = form;
    }
}
