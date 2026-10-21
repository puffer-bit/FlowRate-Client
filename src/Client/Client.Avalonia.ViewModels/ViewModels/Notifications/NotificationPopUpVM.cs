using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Client.Services;
using Client.Avalonia.Styles.Animation;
using ReactiveUI;
using ReactiveUI.Primitives;

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
    
    private string? _tag;
    public string? Tag
    {
        get => _tag;
        set => this.RaiseAndSetIfChanged(ref _tag, value);
    }
    
    private PopUpType _type;
    public PopUpType Type
    {
        get => _type;
        set
        {
            this.RaiseAndSetIfChanged(ref _type, value);
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

    private string? _message;
    public string? Message
    {
        get => _message;
        set => this.RaiseAndSetIfChanged(ref _message, value);
    }

    private bool _isButtonsAvailable;
    public bool IsButtonsAvailable
    {
        get => _isButtonsAvailable;
        set => this.RaiseAndSetIfChanged(ref _isButtonsAvailable, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => this.RaiseAndSetIfChanged(ref _isLoading, value);
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
