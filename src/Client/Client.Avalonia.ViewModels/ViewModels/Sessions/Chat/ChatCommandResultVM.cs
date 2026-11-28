using System;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Threading;
using Client.Avalonia.Localization;
using Client.Avalonia.Styles.Animation;
using ReactiveUI;
using Shared.Models.DataTransferObjects.ChatSession.Messages.Commands;

namespace Client.ViewModels.Sessions.Chat;

public sealed class ChatCommandResultVM : ReactiveObject
{
    public ChatCommandResult Result { get; }
    public bool IsFlip => Result.Kind == ChatCommandKind.Flip;
    public bool IsRoll => !IsFlip;
    public string Caption => IsFlip ? "/flip" : FormattableString.Invariant($"/roll {Result.Minimum}-{Result.Maximum}");
    public string DisplayValue => IsFlip
        ? Strings.Get(_displayValue == 0 ? "chat.command.flip.heads" : "chat.command.flip.tails")
        : _displayValue.ToString(CultureInfo.InvariantCulture);
    public double TokenScaleX
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = 1;
    public double TokenAngle
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }
    public bool IsAnimating
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }
    private int _displayValue;
    private bool _played;
    private IDisposable? _animation;
    private readonly IObservable<string> _heads;
    private readonly IObservable<string> _tails;

    public ChatCommandResultVM(ChatCommandResult result)
    {
        Result = result;
        _displayValue = result.Value;
        // Strings holds these observables weakly, so a message does not outlive its timeline.
        _heads = Strings.Observe("chat.command.flip.heads");
        _tails = Strings.Observe("chat.command.flip.tails");
        _heads.Subscribe(new LanguageObserver(this));
        _tails.Subscribe(new LanguageObserver(this));
    }

    /// <summary>Called only for a live message. Recycling or message updates never replay it.</summary>
    public void Play()
    {
        if (_played) return;
        _played = true;
        var duration = Motion.Get(MotionDuration.ExtraLong) * 3;
        if (duration <= TimeSpan.Zero) return;
        IsAnimating = true;
        _displayValue = Result.Minimum;
        this.RaisePropertyChanged(nameof(DisplayValue));
        var clock = Stopwatch.StartNew();
        int frame = 0;
        _animation = DispatcherTimer.Run(() =>
        {
            if (clock.Elapsed >= duration || !Motion.IsEnabled)
            {
                Finish();
                return false;
            }
            _displayValue = IsFlip ? frame % 2 : Random.Shared.Next(Result.Minimum, Result.Maximum + 1);
            TokenScaleX = IsFlip ? Math.Cos(frame * Math.PI / 3) : 1;
            TokenAngle = IsRoll ? frame * 30 : 0;
            frame++;
            this.RaisePropertyChanged(nameof(DisplayValue));
            return true;
        }, TimeSpan.FromMilliseconds(60));
    }

    public void Finish()
    {
        _animation?.Dispose();
        _animation = null;
        IsAnimating = false;
        _displayValue = Result.Value;
        TokenScaleX = 1;
        TokenAngle = 0;
        this.RaisePropertyChanged(nameof(DisplayValue));
    }

    private sealed class LanguageObserver(ChatCommandResultVM owner) : IObserver<string>
    {
        public void OnNext(string value) => owner.RaisePropertyChanged(nameof(DisplayValue));
        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }
}
