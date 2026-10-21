using Client.Avalonia.Localization;
using ReactiveUI;
using Client.Core.Users;
using Client.ViewModels.Profile;
using Shared.Enums.User;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Main;

/// <summary>
/// The signed-in user shown at the bottom of the side panel, with its own menu.
/// </summary>
public class UserCardVM : ReactiveObject
{
    public string Username
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = Strings.Get("profile.name.placeholder");

    public string StatusText => UserStatusText.Of(Presence);

    /// <summary>The status the user picked.</summary>
    public UserStatus Status
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(Presence));
            this.RaisePropertyChanged(nameof(StatusText));
            this.RaisePropertyChanged(nameof(StatusIconKey));
        }
    } = UserStatus.Online;

    /// <summary>The status shown on the card: offline while not connected.</summary>
    public UserStatus Presence => IsReady ? Status : UserStatus.Offline;

    /// <summary>The icon of <see cref="Status"/>, for the menu that changes it.</summary>
    public string StatusIconKey => $"Status{Status}Icon";

    public string? AvatarUrl
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public int? UserId
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsReady
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(Presence));
            this.RaisePropertyChanged(nameof(StatusText));
        }
    }

    private bool _wasAttached;

    public ReactiveCommand<RxVoid, RxVoid> EditProfileCommand { get; }
    public ReactiveCommand<int, RxVoid> ViewProfileCommand { get; }
    public ReactiveCommand<UserStatus, RxVoid> SetStatusCommand { get; }

    public UserCardVM(ReactiveCommand<RxVoid, RxVoid> editProfileCommand,
        ReactiveCommand<int, RxVoid> viewProfileCommand, ReactiveCommand<UserStatus, RxVoid> setStatusCommand)
    {
        EditProfileCommand = editProfileCommand;
        ViewProfileCommand = viewProfileCommand;
        SetStatusCommand = setStatusCommand;

        // The card lives as long as the main window, so the subscription is never dropped
        Strings.LanguageChanged += () =>
        {
            this.RaisePropertyChanged(nameof(StatusText));

            // Until the first login the name is a placeholder, which is a text of the interface
            if (!_wasAttached)
                Username = Strings.Get("profile.name.placeholder");
        };
    }

    public void AttachUser(UserModel user)
    {
        _wasAttached = true;
        UserId = user.Id;
        Username = user.Username;
        AvatarUrl = user.AvatarUrl;
        Status = user.Status;
        IsReady = true;
    }

    public void DetachUser()
    {
        IsReady = false;
        UserId = null;
    }
}
