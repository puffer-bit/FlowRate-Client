using System;
using ReactiveUI;
using Client.Core.Coordinator;
using Client.ViewModels.Main;

namespace Client.ViewModels.ServerSettings;

/// <summary>
/// What the server is called and tells connecting users, and what it reported about itself on connecting.
/// </summary>
public class ServerInfoTabVM : ReactiveObject
{
    public CoordinatorSettingsFormVM Form { get; }

    public string? Address { get; }

    public string? Version { get; }

    public string OnlineText { get; }

    public string? FirstStartedText { get; }

    public string? LastStartedText { get; }

    public ServerInfoTabVM(ServerTitleVM server, CoordinatorSessionModel? coordinator, CoordinatorSettingsFormVM form)
    {
        Form = form;
        Address = server.Address;
        OnlineText = server.OnlineText;
        Version = coordinator?.ServerVersion;
        FirstStartedText = coordinator == null ? null : FormatTime(coordinator.FirstLaunchTime);
        LastStartedText = coordinator == null ? null : FormatTime(coordinator.CurrentLaunchTime);
    }

    // The server sends its times in UTC
    private static string FormatTime(DateTime time) =>
        DateTime.SpecifyKind(time, DateTimeKind.Utc).ToLocalTime().ToString("dd.MM.yyyy HH:mm");
}
