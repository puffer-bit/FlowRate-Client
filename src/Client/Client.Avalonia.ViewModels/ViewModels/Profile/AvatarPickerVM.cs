using Client.Services;
using Client.Avalonia.Localization;
using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Client.ViewModels.Sessions.Chat;
using ReactiveUI;
using Shared.Enums.User;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Profile;

/// <summary>
/// An image the user picked for an avatar, shared by the profile dialogs.
/// </summary>
public sealed class AvatarPickerVM : ReactiveObject, IDisposable
{
    private const long AvatarMaxFileSize = 8 * 1024 * 1024;

    private readonly IAvatarService _avatars;

    public Bitmap? Avatar
    {
        get;
        private set
        {
            var previous = field;
            this.RaiseAndSetIfChanged(ref field, value);

            if (!ReferenceEquals(previous, value))
                previous?.Dispose();
        }
    }

    /// <summary>
    /// The picked image as it goes to the server; null when none is picked.
    /// </summary>
    public PreparedAvatar? Picked { get; private set; }

    public string? AvatarError
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// Asks the view for an image file, or gets null when the user changed their mind.
    /// </summary>
    public Interaction<RxVoid, PickedFile?> PickAvatarFile { get; } = new();

    public ReactiveCommand<RxVoid, RxVoid> ChooseAvatarCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveAvatarCommand { get; }

    public AvatarPickerVM(IAvatarService avatars)
    {
        _avatars = avatars;

        ChooseAvatarCommand = ReactiveCommand.CreateFromTask(ChooseAvatarAsync);
        RemoveAvatarCommand = ReactiveCommand.Create(Clear,
            this.WatchProperty(nameof(Avatar), vm => vm.Avatar).Select(avatar => avatar != null));
    }

    public void Clear()
    {
        Picked = null;
        Avatar = null;
        AvatarError = null;
    }

    /// <summary>
    /// Tells under the avatar why the server did not take it; nothing for a result that needs no words there.
    /// </summary>
    public void ShowResult(SetAvatarResult result) => AvatarError = result switch
    {
        SetAvatarResult.NoError => null,
        SetAvatarResult.UploadFailed or SetAvatarResult.UploadNotFound => Strings.Get("profile.avatar.uploadFailed"),
        SetAvatarResult.NotAllowed => Strings.Get("profile.avatar.notAllowed"),
        SetAvatarResult.UserNotFound => Strings.Get("profile.name.userNotConnected"),
        SetAvatarResult.TimedOut => Strings.Get("profile.name.timedOut"),
        // The main window tells why; the picture stays picked for another try
        SetAvatarResult.Rejected => null,
        _ => Strings.Format("profile.avatar.notChanged", ("reason", result))
    };

    private async Task ChooseAvatarAsync()
    {
        var file = await PickAvatarFile.Handle(RxVoid.Default);

        if (file == null)
            return;

        AvatarError = null;

        if (file.Size > AvatarMaxFileSize)
        {
            AvatarError = Strings.Format("profile.avatar.tooLarge", ("size", AvatarMaxFileSize / (1024 * 1024)));
            return;
        }

        var prepared = await _avatars.PrepareAsync(file.Open);
        if (prepared == null)
        {
            AvatarError = Strings.Get("profile.avatar.notAnImage");
            return;
        }

        Picked = prepared;
        Avatar = prepared.Bitmap;
    }

    public void Dispose()
    {
        Picked = null;
        Avatar = null;
    }
}
