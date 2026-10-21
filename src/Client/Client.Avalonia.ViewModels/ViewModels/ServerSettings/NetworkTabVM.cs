using ReactiveUI;

namespace Client.ViewModels.ServerSettings;

/// <summary>
/// Where the coordinator listens, how it tells a client is gone, and the STUN and TURN servers it hands out.
/// </summary>
public class NetworkTabVM : ReactiveObject
{
    public CoordinatorSettingsFormVM Form { get; }

    public NetworkTabVM(CoordinatorSettingsFormVM form)
    {
        Form = form;
    }
}
