namespace Client.Avalonia.Localization;

/// <summary>
/// One text of <see cref="Strings"/> that is published again when the language changes.
/// <see cref="Strings"/> holds it weakly and every subscription holds it strongly, so it lives as
/// long as something listens to it.
/// </summary>
internal sealed class TextObservable : IObservable<string>
{
    private readonly Func<string?> _read;
    private readonly List<IObserver<string>> _observers = [];

    public TextObservable(Func<string?> read)
    {
        _read = read;
    }

    public IDisposable Subscribe(IObserver<string> observer)
    {
        lock (_observers)
            _observers.Add(observer);

        observer.OnNext(_read()!);
        return new Subscription(this, observer);
    }

    public void Publish()
    {
        IObserver<string>[] observers;
        lock (_observers)
            observers = _observers.ToArray();

        var text = _read()!;
        foreach (var observer in observers)
            observer.OnNext(text);
    }

    private sealed class Subscription(TextObservable owner, IObserver<string> observer) : IDisposable
    {
        public void Dispose()
        {
            lock (owner._observers)
                owner._observers.Remove(observer);
        }
    }
}
