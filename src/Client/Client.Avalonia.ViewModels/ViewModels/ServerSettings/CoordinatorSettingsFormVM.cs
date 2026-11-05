using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using Client.Core.Coordinator;
using ReactiveUI;

namespace Client.ViewModels.ServerSettings;

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
