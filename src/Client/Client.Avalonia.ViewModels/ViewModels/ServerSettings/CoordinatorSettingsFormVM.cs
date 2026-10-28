using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using ReactiveUI;
using Client.Avalonia.Localization;
using Client.Core.Coordinator;
using Shared.Enums;

namespace Client.ViewModels.ServerSettings;

/// <summary>
/// What the server password becomes on saving; the index of the choice in the combo box of the view.
/// </summary>
public enum PasswordChange
{
    Keep = 0,
    Set = 1,
    Remove = 2
}

/// <summary>
/// The settings of the coordinator being edited, shared by the tabs that show them. Numbers are decimals because
/// that is what the number boxes bind to, null while a box is empty; the server checks every value on saving.
/// </summary>
public sealed class CoordinatorSettingsFormVM : ReactiveObject
{
    private const decimal Megabyte = 1024 * 1024;

    // The values the form was filled with, to tell whether anything was changed since
    private string _loadedState = string.Empty;
    private bool _isLoading;

    /// <summary>The settings the form was filled with; null before the first load.</summary>
    public CoordinatorSettingsModel? Loaded
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsDirty
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>A new address or port, which restarts the coordinator and drops every connection.</summary>
    public bool MovesEndpoint
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string Name
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    public bool IsMotdEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string MotdText
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    public string Ip
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    public decimal? Port
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsPasswordProtected => Loaded?.IsPasswordProtected ?? false;

    public int PasswordChangeIndex
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsNewPasswordShown));
        }
    }

    public bool IsNewPasswordShown => (PasswordChange)PasswordChangeIndex == PasswordChange.Set;

    public string NewPassword
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    public decimal? MaxOnlineUsers
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public decimal? MaxUsers
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>One address on a line.</summary>
    public string AllowedIps
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    /// <inheritdoc cref="AllowedIps"/>
    public string BannedIps
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    public decimal? HeartbeatIntervalSeconds
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public decimal? HeartbeatTimeoutSeconds
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public decimal? MaxFileSizeMegabytes
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>0 leaves the storage unlimited.</summary>
    public decimal? StorageQuotaMegabytes
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public decimal? MaxUnattachedUploadsPerUser
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public decimal? UnattachedUploadLifetimeHours
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public decimal? MediaTokenLifetimeMinutes
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>Every node of the server, the ones given to the coordinator checked.</summary>
    public IReadOnlyList<SfuNodeOptionVM> SfuNodes
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = [];

    public CoordinatorSettingsFormVM()
    {
        // The form lives as long as its dialog, so the subscription is never dropped
        Changed.Where(change => change.PropertyName is not (nameof(IsDirty) or nameof(MovesEndpoint) or nameof(Loaded)))
            .Subscribe(_ => Refresh());
    }

    /// <summary>Fills the form with the settings as they are on the server; whatever was edited is dropped.</summary>
    public void Load(CoordinatorSettingsModel settings, IReadOnlyList<SfuNodeModel> sfuNodes)
    {
        _isLoading = true;
        Loaded = settings;

        Name = settings.Name;
        IsMotdEnabled = settings.IsMotdEnabled;
        MotdText = settings.MotdText ?? string.Empty;
        Ip = settings.Ip;
        Port = settings.Port;
        PasswordChangeIndex = (int)PasswordChange.Keep;
        NewPassword = string.Empty;
        MaxOnlineUsers = settings.MaxOnlineUsers;
        MaxUsers = settings.MaxUsers;
        AllowedIps = string.Join(Environment.NewLine, settings.AllowedIps);
        BannedIps = string.Join(Environment.NewLine, settings.BannedIps);
        HeartbeatIntervalSeconds = settings.HeartbeatIntervalSeconds;
        HeartbeatTimeoutSeconds = settings.HeartbeatTimeoutSeconds;
        MaxFileSizeMegabytes = Math.Round(settings.MaxFileSizeBytes / Megabyte);
        StorageQuotaMegabytes = Math.Round(settings.StorageQuotaBytes / Megabyte);
        MaxUnattachedUploadsPerUser = settings.MaxUnattachedUploadsPerUser;
        UnattachedUploadLifetimeHours = settings.UnattachedUploadLifetimeHours;
        MediaTokenLifetimeMinutes = settings.MediaTokenLifetimeMinutes;

        // Nodes the coordinator names but the server no longer has are kept, so saving does not drop them unseen
        var nodes = sfuNodes.Select(node => new SfuNodeOptionVM(node, settings.SfuNodeIds.Contains(node.Id))).ToList();
        foreach (var id in settings.SfuNodeIds.Where(id => sfuNodes.All(node => node.Id != id)))
            nodes.Add(new SfuNodeOptionVM(new SfuNodeModel(id, $"#{id}", string.Empty, 0, false), true));
        foreach (var node in nodes)
            node.Changed.Subscribe(_ => Refresh());
        SfuNodes = nodes;

        this.RaisePropertyChanged(nameof(IsPasswordProtected));
        _loadedState = Fingerprint(ToUpdate());
        _isLoading = false;
        Refresh();
    }

    /// <summary>The settings as the form holds them now, to be sent to the server.</summary>
    public CoordinatorSettingsUpdate ToUpdate() => new()
    {
        Name = Name.Trim(),
        Ip = Ip.Trim(),
        Port = ToInt(Port),
        NewPassword = (PasswordChange)PasswordChangeIndex == PasswordChange.Set ? NewPassword : null,
        RemovePassword = (PasswordChange)PasswordChangeIndex == PasswordChange.Remove,
        MaxOnlineUsers = ToInt(MaxOnlineUsers),
        MaxUsers = ToInt(MaxUsers),
        IsMotdEnabled = IsMotdEnabled,
        MotdText = OrNull(MotdText),
        AllowedIps = ToLines(AllowedIps),
        BannedIps = ToLines(BannedIps),
        HeartbeatIntervalSeconds = ToInt(HeartbeatIntervalSeconds),
        HeartbeatTimeoutSeconds = ToInt(HeartbeatTimeoutSeconds),
        MaxFileSizeBytes = (long)((MaxFileSizeMegabytes ?? 0) * Megabyte),
        StorageQuotaBytes = (long)((StorageQuotaMegabytes ?? 0) * Megabyte),
        MaxUnattachedUploadsPerUser = ToInt(MaxUnattachedUploadsPerUser),
        UnattachedUploadLifetimeHours = ToInt(UnattachedUploadLifetimeHours),
        MediaTokenLifetimeMinutes = ToInt(MediaTokenLifetimeMinutes),
        RateLimits = Loaded?.RateLimits ?? new CoordinatorRateLimits(),
        SfuNodeIds = SfuNodes.Where(node => node.IsSelected).Select(node => node.Id).ToList()
    };

    private void Refresh()
    {
        if (Loaded == null || _isLoading)
            return;

        IsDirty = Fingerprint(ToUpdate()) != _loadedState;
        MovesEndpoint = Ip.Trim() != Loaded.Ip || ToInt(Port) != Loaded.Port;
    }

    // Empty boxes are sent as 0 and refused by the server with the reason
    private static int ToInt(decimal? value) => value is { } number ? (int)number : 0;

    private static string? OrNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> ToLines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    // Records compare their lists by reference, so the values are written out instead
    private static string Fingerprint(CoordinatorSettingsUpdate update) => string.Join('\u001f',
        update with { AllowedIps = [], BannedIps = [], SfuNodeIds = [] },
        string.Join(',', update.AllowedIps), string.Join(',', update.BannedIps), string.Join(',', update.SfuNodeIds));
}

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
