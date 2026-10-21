using System;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace Client.Services;

/// <summary>
/// Property changes as observables. Stands in for WhenAnyValue of ReactiveUI, which finds the property
/// through an expression tree and reflection and so does not survive trimming and NativeAOT: here the
/// name comes from nameof and the value from a plain delegate.
/// </summary>
public static class PropertyWatch
{
    /// <summary>
    /// The value of a property: the current one on subscription, then every new one.
    /// </summary>
    public static IObservable<TValue> WatchProperty<TSender, TValue>(this TSender sender, string propertyName,
        Func<TSender, TValue> read) where TSender : class, INotifyPropertyChanged
    {
        return sender.WatchProperties(propertyName).Select(read).DistinctUntilChanged();
    }

    /// <summary>
    /// The object itself: once on subscription, then every time one of the named properties changes.
    /// For a value made of several properties, to be read from the object in a Select.
    /// </summary>
    public static IObservable<TSender> WatchProperties<TSender>(this TSender sender, params string[] propertyNames)
        where TSender : class, INotifyPropertyChanged
    {
        return Observable.Create<TSender>(observer =>
        {
            void OnPropertyChanged(object? _, PropertyChangedEventArgs e)
            {
                // No name stands for "every property has changed"
                if (string.IsNullOrEmpty(e.PropertyName) || propertyNames.Contains(e.PropertyName))
                    observer.OnNext(sender);
            }

            sender.PropertyChanged += OnPropertyChanged;
            observer.OnNext(sender);

            return Disposable.Create(() => sender.PropertyChanged -= OnPropertyChanged);
        });
    }
}
