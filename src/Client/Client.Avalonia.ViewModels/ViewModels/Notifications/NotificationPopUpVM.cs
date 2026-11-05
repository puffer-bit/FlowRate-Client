using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Client.Avalonia.Styles.Animation;
using Client.Services;
using ReactiveUI;

using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Notifications;

public class NotificationPopUpVM : ReactiveObject
{
    public enum PopUpType
    {
        Info,
        Warning,
        Error,
        Loading,
        Success,
        Question
    }
    private readonly TaskCompletionSource<bool> _tcs = new();

    public Task<bool> Result => _tcs.Task;

    public string? Tag
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public PopUpType Type
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IconKey));
        }
    }

    /// <summary>Key of a DrawingImage resource from Client.Avalonia.Assets/IconImages.axaml.</summary>
    public string IconKey => IconKeys.Of(Type switch
    {
        PopUpType.Warning => IconType.Warning,
        PopUpType.Success => IconType.Success,
        PopUpType.Error => IconType.Error,
        PopUpType.Question => IconType.Question,
        _ => IconType.Info
    });

    public string? Message
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsButtonsAvailable
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsLoading
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// The notification is playing its leave animation and is removed from the list right after it.
    /// </summary>
    public bool IsLeaving
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> CloseCommand { get; }
    public ReactiveCommand<RxVoid, bool> YesCommand { get; }
    public ReactiveCommand<RxVoid, bool> NoCommand { get; }

    public NotificationPopUpVM(PopUpType type, string? message, ObservableCollection<NotificationPopUpVM> notificationsCollection, string? tag = null)
    {
        CloseCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            IsLeaving = true;
            await Task.Delay(Presence.LeaveTime);
            notificationsCollection.Remove(this);
        });
        YesCommand = ReactiveCommand.Create(() => _tcs.TrySetResult(true));
        NoCommand  = ReactiveCommand.Create(() => _tcs.TrySetResult(false));

        Type = type;
        Tag = tag;
        Message = message;

        IsLoading = type == PopUpType.Loading;
        IsButtonsAvailable = type == PopUpType.Question;
    }
}
