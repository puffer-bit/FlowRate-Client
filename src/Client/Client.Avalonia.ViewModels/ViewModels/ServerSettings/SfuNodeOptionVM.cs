using System;
using Client.Avalonia.Localization;
using Client.Core.Coordinator;
using ReactiveUI;
using Shared.Enums;

namespace Client.ViewModels.ServerSettings;

/// <summary>A node the coordinator can be given, checked when it is.</summary>
public sealed class SfuNodeOptionVM : ReactiveObject
{
    public int Id { get; }
    public string Name { get; }
    public string Address { get; }

    /// <summary>The server keeps the node but sends no sessions through it.</summary>
    public bool IsDisabled { get; }

    /// <summary>
    /// The server's ping to the node when the settings were loaded. It is not refreshed while the page is open,
    /// so it reads as the state of the moment of opening.
    /// </summary>
    public TimeSpan? RoundTrip { get; }

    /// <summary>Whether the server had the node's connection state; not for a disabled or unknown node.</summary>
    public bool HasConnection { get; }

    /// <summary>The server cannot send sessions through the node right now.</summary>
    public bool IsConnectionProblem { get; }

    public string ConnectionText { get; } = string.Empty;

    /// <summary>Why the node is not connected, as the server told it; null when there is nothing to tell.</summary>
    public string? ConnectionError { get; }

    public bool IsSelected
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public SfuNodeOptionVM(SfuNodeModel node, bool isSelected)
    {
        Id = node.Id;
        Name = node.Name;
        Address = node.Port > 0 ? $"{node.Host}:{node.Port}" : node.Host;
        IsDisabled = !node.IsEnabled;
        IsSelected = isSelected;

        if (node.Connection is { } connection && node.IsEnabled)
        {
            HasConnection = true;
            RoundTrip = connection.State == SFUNodeConnectionState.Connected ? connection.RoundTrip : null;
            IsConnectionProblem = connection.State is SFUNodeConnectionState.Unresponsive
                or SFUNodeConnectionState.Disconnected;
            ConnectionText = Strings.Get(connection.State switch
            {
                SFUNodeConnectionState.Connected => "serverSettings.sfu.state.connected",
                SFUNodeConnectionState.Unresponsive => "serverSettings.sfu.state.unresponsive",
                SFUNodeConnectionState.Disconnected => "serverSettings.sfu.state.disconnected",
                _ => "serverSettings.sfu.state.connecting"
            });
            ConnectionError = string.IsNullOrWhiteSpace(connection.Error) ? null : connection.Error;
        }
    }
}
