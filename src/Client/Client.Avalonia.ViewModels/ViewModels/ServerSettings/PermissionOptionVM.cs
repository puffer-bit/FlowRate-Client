using System;
using ReactiveUI;
using Shared.Enums;

namespace Client.ViewModels.ServerSettings;

/// <summary>One permission in the role editor.</summary>
public sealed class PermissionOptionVM(Permission permission, string title, string description, Action onChanged)
    : ReactiveObject
{
    public Permission Permission { get; } = permission;
    public string Title { get; } = title;
    public string Description { get; } = description;

    public bool IsChecked
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            onChanged();
        }
    }

    public bool IsEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }
}
