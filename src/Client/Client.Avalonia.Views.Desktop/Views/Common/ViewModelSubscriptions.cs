using System;
using System.Reactive.Disposables;
using Avalonia;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;

namespace Client.Views.Common;

internal static class ViewModelSubscriptions
{
    /// <summary>
    /// Calls <paramref name="subscribe"/> for every view model <paramref name="view"/> shows. A data template
    /// reuses the view for the next view model, and the view models outlive it, so whatever is added to the
    /// disposable passed along ends as soon as another view model is shown.
    /// </summary>
    /// <returns>The subscriptions, to be disposed together with the view activation.</returns>
    public static IDisposable WhenViewModelChanged<TViewModel>(this ViewControl<TViewModel> view,
        Action<TViewModel, MultipleDisposable> subscribe) where TViewModel : class
    {
        var current = new SerialDisposable();

        // The view model is the data context; anything else in it, or nothing, is not a view model to show
        var changes = ObservableExtensions.Subscribe(
            view.GetObservable(StyledElement.DataContextProperty).Where(context => context is TViewModel), vm =>
        {
            var subscriptions = new MultipleDisposable();
            current.Disposable = subscriptions;
            subscribe((TViewModel)vm!, subscriptions);
        });

        return new MultipleDisposable { changes, current };
    }
}
