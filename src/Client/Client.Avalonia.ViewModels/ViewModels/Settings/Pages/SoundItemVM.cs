using System;
using Client.Application.Sounds;
using ReactiveUI;

using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Settings.Pages;

public class SoundItemVM : ReactiveObject
{
    public SoundEvent Event { get; }
    public string Title { get; }
    public ReactiveCommand<RxVoid, RxVoid> PlayCommand { get; }

    public bool IsEnabled
    {
        get;
        set
        {
            if (field == value)
                return;

            this.RaiseAndSetIfChanged(ref field, value);
            _changed();
        }
    } = true;

    private readonly Action _changed;

    public SoundItemVM(SoundEvent soundEvent, string title, ISoundService soundService, Action changed)
    {
        Event = soundEvent;
        Title = title;
        _changed = changed;
        PlayCommand = ReactiveCommand.Create(() => soundService.Preview(soundEvent));
    }
}
