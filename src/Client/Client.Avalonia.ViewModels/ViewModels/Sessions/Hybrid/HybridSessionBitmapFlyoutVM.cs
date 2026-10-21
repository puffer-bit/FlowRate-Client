using ReactiveUI;
using ReactiveUI.Primitives;

namespace Client.ViewModels.Sessions.Hybrid;

public class HybridSessionBitmapFlyoutVM : ReactiveObject
{
    public float MaxVolume
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = 1;
    
    public float Volume
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
        }
    }
    
    public bool IsOverdriveEnabled
    {
        get;
        set
        {
            if (value)
                MaxVolume = 4;
            else
                MaxVolume = 1;
            this.RaiseAndSetIfChanged(ref field, value);
        }
    }

    public bool IsMuted
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> ToggleMute { get; set; }
    public ReactiveCommand<RxVoid, RxVoid> ToggleOverdrive { get; set; }

    private float PreMuteVolume { get; set; }

    public HybridSessionBitmapFlyoutVM()
    {
        ToggleMute = ReactiveCommand.Create(ToggleMuteState);
        ToggleOverdrive = ReactiveCommand.Create(ToggleOverdriveState);
        
        Volume = (float)0.4;
    }

    private void ToggleMuteState()
    {
        if (!IsMuted)
        {
            PreMuteVolume = Volume;
            Volume = 0;
            IsMuted = true;
        }
        else
        {
            Volume = PreMuteVolume;
            IsMuted = false;
        }
    }

    private void ToggleOverdriveState()
    {
        if (!IsOverdriveEnabled)
        {
            MaxVolume = (float)1.5;
            IsOverdriveEnabled = true;
        }
        else
        {
            MaxVolume = 1;
            IsOverdriveEnabled = false;
        }
    }
}