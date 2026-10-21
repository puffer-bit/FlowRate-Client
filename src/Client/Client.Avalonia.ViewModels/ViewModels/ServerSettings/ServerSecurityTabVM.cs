using ReactiveUI;

namespace Client.ViewModels.ServerSettings;

/// <summary>
/// Who may connect to the server: its password and the addresses it lets in or keeps out.
/// </summary>
public class ServerSecurityTabVM : ReactiveObject
{
    public CoordinatorSettingsFormVM Form { get; }

    public ServerSecurityTabVM(CoordinatorSettingsFormVM form)
    {
        Form = form;
    }
}
