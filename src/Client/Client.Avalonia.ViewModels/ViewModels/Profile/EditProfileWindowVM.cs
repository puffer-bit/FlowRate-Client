using Client.Core.Coordinator;
using Client.Services;
using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Profile;

/// <summary>
/// The dialog where the user changes their name and avatar.
/// </summary>
public sealed class EditProfileWindowVM : ReactiveObject, IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly ObservableAsPropertyHelper<bool> _isBusy;
    private readonly string _currentName;
    private readonly string? _currentAvatarUrl;
    private readonly ReactiveCommand<string, RequestResult> _changeUsernameCommand;
    private readonly ReactiveCommand<PreparedAvatar, (RequestResult Result, string? AvatarUrl)> _setAvatarCommand;
    private readonly ReactiveCommand<int, RequestResult> _resetAvatarCommand;

    public string Name
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string? NameError
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// Set when the avatar goes back to the default one on save.
    /// </summary>
    public bool IsAvatarReset
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(ShownAvatarUrl));
        }
    }

    /// <summary>
    /// The avatar the user has now, shown until another one is picked or it is removed.
    /// </summary>
    public string? ShownAvatarUrl => IsAvatarReset ? null : _currentAvatarUrl;

    public AvatarPickerVM AvatarPicker { get; }

    public bool IsBusy => _isBusy.Value;

    public int? UserId { get; }
    public string NameHint => UsernameRules.Hint;

    public Interaction<RxVoid, RxVoid> CloseWindow { get; } = new();

    public ReactiveCommand<RxVoid, RxVoid> RemoveAvatarCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CloseCommand { get; }

    public EditProfileWindowVM(string currentName, int? userId, string? currentAvatarUrl, IAvatarService avatars,
        ReactiveCommand<string, RequestResult> changeUsernameCommand,
        ReactiveCommand<PreparedAvatar, (RequestResult Result, string? AvatarUrl)> setAvatarCommand,
        ReactiveCommand<int, RequestResult> resetAvatarCommand)
    {
        _currentName = currentName;
        _currentAvatarUrl = currentAvatarUrl;
        _changeUsernameCommand = changeUsernameCommand;
        _setAvatarCommand = setAvatarCommand;
        _resetAvatarCommand = resetAvatarCommand;
        Name = currentName;
        UserId = userId;
        AvatarPicker = new AvatarPickerVM(avatars);

        var avatar = AvatarPicker.WatchProperty(nameof(AvatarPickerVM.Avatar), picker => picker.Avatar);
        var hasChanges = this.WatchProperties(nameof(Name), nameof(IsAvatarReset))
            .CombineLatest(avatar, (vm, picked) => UsernameRules.Validate(vm.Name) == null
                && (vm.Name.Trim() != _currentName || vm.IsAvatarReset || picked != null));
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync, hasChanges);

        // Takes the picked image away first, and then the avatar the user already has
        RemoveAvatarCommand = ReactiveCommand.Create(RemoveAvatar,
            this.WatchProperty(nameof(IsAvatarReset), vm => vm.IsAvatarReset)
                .CombineLatest(avatar, (reset, picked) => picked != null || (!reset && _currentAvatarUrl != null)));

        // A picked image replaces the reset
        _disposables.Add(avatar.Where(picked => picked != null).Subscribe(_ => IsAvatarReset = false));

        CloseCommand = ReactiveCommand.CreateFromTask(async () => { await CloseWindow.Handle(RxVoid.Default); },
            SaveCommand.IsExecuting.Select(busy => !busy));

        _isBusy = SaveCommand.IsExecuting.ToProperty(this, nameof(IsBusy));

        _disposables.Add(this.WatchProperty(nameof(Name), vm => vm.Name).Skip(1)
            .Subscribe(name => NameError = UsernameRules.Validate(name)));
    }

    private async Task SaveAsync()
    {
        NameError = UsernameRules.Validate(Name);

        if (NameError != null)
            return;

        var name = Name.Trim();

        if (name != _currentName)
        {
            var result = await _changeUsernameCommand.Execute(name);

            NameError = UsernameRules.DescribeResult(result);

            if (!result.IsSuccess)
                return;
        }

        if (AvatarPicker.Picked is { } picked)
        {
            var (result, _) = await _setAvatarCommand.Execute(picked);

            AvatarPicker.ShowResult(result);

            if (!result.IsSuccess)
                return;
        }
        else if (IsAvatarReset && UserId is { } userId)
        {
            var result = await _resetAvatarCommand.Execute(userId);

            AvatarPicker.ShowResult(result);

            if (!result.IsSuccess)
                return;
        }

        await CloseWindow.Handle(RxVoid.Default);
    }

    private void RemoveAvatar()
    {
        if (AvatarPicker.Avatar != null)
            AvatarPicker.Clear();
        else
            IsAvatarReset = true;
    }

    public void Dispose()
    {
        _disposables.Dispose();
        _isBusy.Dispose();
        AvatarPicker.Dispose();
    }
}
