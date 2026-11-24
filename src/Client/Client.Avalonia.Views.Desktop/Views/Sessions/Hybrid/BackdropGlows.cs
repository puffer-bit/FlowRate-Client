using System;
using System.Collections.Specialized;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Client.Avalonia.Styles.Animation;

namespace Client.Views.Sessions.Hybrid;

/// <summary>
/// Moves the glows of the session backdrop along their closed paths while the backdrop is shown and
/// animations are switched on. A style animation would redraw the whole backdrop on every frame of
/// the display, 144 times a second on a fast one, though a glow moves less than a pixel a frame;
/// the timer moves them 20 times a second, which looks the same.
/// </summary>
internal sealed class BackdropGlows : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(50);

    private readonly Control _backdrop;
    private readonly Glow[] _glows;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();

    /// <param name="backdrop">Tracked by <see cref="ShownState"/>.</param>
    public BackdropGlows(Control backdrop, params Glow[] glows)
    {
        _backdrop = backdrop;
        _glows = glows;
        _timer = new DispatcherTimer(Interval, DispatcherPriority.Background, (_, _) => Move());

        _backdrop.Classes.CollectionChanged += OnClassesChanged;
        Motion.Changed += Update;
        Update();
    }

    public void Dispose()
    {
        _backdrop.Classes.CollectionChanged -= OnClassesChanged;
        Motion.Changed -= Update;
        _timer.Stop();
    }

    private void OnClassesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Update();

    private void Update()
    {
        var isMoving = Motion.IsEnabled && _backdrop.Classes.Contains(ShownState.PseudoClass);

        if (isMoving == _timer.IsEnabled)
            return;

        if (isMoving)
        {
            _clock.Start();
            _timer.Start();
            Move();
            return;
        }

        _timer.Stop();
        _clock.Reset();

        foreach (var glow in _glows)
        {
            glow.Transform.X = 0;
            glow.Transform.Y = 0;
        }
    }

    private void Move()
    {
        var elapsed = _clock.Elapsed.TotalSeconds;

        foreach (var glow in _glows)
            glow.MoveTo(elapsed);
    }

    /// <summary>A glow and its path, which ends where it starts.</summary>
    /// <param name="Points">Cues from 0 to 1 with the offsets the glow passes at them.</param>
    public sealed record Glow(TranslateTransform Transform, TimeSpan Duration, params (double Cue, double X, double Y)[] Points)
    {
        public void MoveTo(double seconds)
        {
            var progress = seconds % Duration.TotalSeconds / Duration.TotalSeconds;
            // Sine ease in and out over the whole loop, as the style animation had
            var eased = (1 - Math.Cos(Math.PI * progress)) / 2;

            for (var i = 1; i < Points.Length; i++)
            {
                var (cue, x, y) = Points[i];

                if (eased > cue && i < Points.Length - 1)
                    continue;

                var (fromCue, fromX, fromY) = Points[i - 1];
                var part = Math.Clamp((eased - fromCue) / (cue - fromCue), 0, 1);
                Transform.X = fromX + (x - fromX) * part;
                Transform.Y = fromY + (y - fromY) * part;
                return;
            }
        }
    }
}
