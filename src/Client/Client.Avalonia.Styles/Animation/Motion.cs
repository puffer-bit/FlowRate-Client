using Avalonia.Animation.Easings;

namespace Client.Avalonia.Styles.Animation;

/// <summary>
/// The lengths an animation of the interface can have. Markup and code name one of them instead of
/// a time of their own, so everything that moves follows the same settings.
/// </summary>
public enum MotionDuration
{
    /// <summary>Feedback to the pointer: hover and press.</summary>
    Brief,

    /// <summary>A control changes its state; a popup, a dialog or a page comes and goes.</summary>
    Short,

    /// <summary>An element fades in or out, or folds away.</summary>
    Medium,

    /// <summary>An element appears or moves to another place.</summary>
    Long,

    /// <summary>A slow fade that is meant to be noticed.</summary>
    ExtraLong
}

/// <summary>
/// The curves an animation of the interface can follow. Colors are blended evenly and name none.
/// </summary>
public enum MotionEase
{
    /// <summary>A change that goes both ways: a fade, a fold, a shift between two states.</summary>
    Standard,

    /// <summary>An element comes in and settles: fast at first, slowing down into its place.</summary>
    Enter,

    /// <summary>An element leaves: slow at first, then gone.</summary>
    Exit
}

/// <summary>
/// The one source of animation times and curves. With animations switched off every duration is zero, which
/// transitions and keyframe animations take as a jump to the final value; code that waits for an
/// animation waits for the same duration and so does not wait either.
/// </summary>
public static class Motion
{
    private static readonly List<WeakReference<DurationObservable>> Observables = [];
    // How many observables were alive after the last cleanup: the list is cleaned again once it has doubled
    private static int _aliveCount = 16;

    private static double _durationScale = 1;

    // Easings keep no state, so every animation shares these
    private static readonly Easing StandardEase = new CubicEaseInOut();
    private static readonly Easing EnterEase = new CubicEaseOut();
    private static readonly Easing ExitEase = new CubicEaseIn();

    public static bool IsEnabled { get; private set; } = true;

    /// <summary>
    /// Raised on the thread of <see cref="Apply"/> after the durations have changed.
    /// </summary>
    public static event Action? Changed;

    /// <param name="isEnabled">Whether the interface is animated at all.</param>
    /// <param name="durationScale">What every duration is multiplied by: above one is slower.</param>
    public static void Apply(bool isEnabled, double durationScale)
    {
        IsEnabled = isEnabled;
        _durationScale = durationScale;

        List<DurationObservable> alive;
        lock (Observables)
            alive = CollectAlive();

        foreach (var observable in alive)
            observable.Publish();

        Changed?.Invoke();
    }

    public static TimeSpan Get(MotionDuration duration)
    {
        if (!IsEnabled)
            return TimeSpan.Zero;

        var milliseconds = duration switch
        {
            MotionDuration.Brief => 120,
            MotionDuration.Short => 150,
            MotionDuration.Medium => 200,
            MotionDuration.Long => 250,
            MotionDuration.ExtraLong => 400,
            _ => 150
        };

        return TimeSpan.FromMilliseconds(milliseconds * _durationScale);
    }

    public static Easing Ease(MotionEase ease)
    {
        return ease switch
        {
            MotionEase.Enter => EnterEase,
            MotionEase.Exit => ExitEase,
            _ => StandardEase
        };
    }

    /// <summary>
    /// The duration now and after every change of the settings. The observable is held weakly here
    /// and strongly by its subscriptions, so it lives as long as the animation it is bound to.
    /// </summary>
    public static IObservable<TimeSpan> Observe(MotionDuration duration)
    {
        var observable = new DurationObservable(duration);

        lock (Observables)
        {
            if (Observables.Count >= _aliveCount * 2)
                CollectAlive();

            Observables.Add(new WeakReference<DurationObservable>(observable));
        }

        return observable;
    }

    private static List<DurationObservable> CollectAlive()
    {
        var alive = new List<DurationObservable>(Observables.Count);

        Observables.RemoveAll(reference =>
        {
            if (!reference.TryGetTarget(out var observable))
                return true;

            alive.Add(observable);
            return false;
        });

        _aliveCount = Math.Max(alive.Count, 16);
        return alive;
    }

    private sealed class DurationObservable(MotionDuration duration) : IObservable<TimeSpan>
    {
        private readonly List<IObserver<TimeSpan>> _observers = [];

        public IDisposable Subscribe(IObserver<TimeSpan> observer)
        {
            lock (_observers)
                _observers.Add(observer);

            observer.OnNext(Get(duration));
            return new Subscription(this, observer);
        }

        public void Publish()
        {
            IObserver<TimeSpan>[] observers;
            lock (_observers)
                observers = _observers.ToArray();

            var value = Get(duration);
            foreach (var observer in observers)
                observer.OnNext(value);
        }

        private sealed class Subscription(DurationObservable owner, IObserver<TimeSpan> observer) : IDisposable
        {
            public void Dispose()
            {
                lock (owner._observers)
                    owner._observers.Remove(observer);
            }
        }
    }
}
