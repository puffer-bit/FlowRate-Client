using System;
using System.Collections.Generic;
using Avalonia.Controls;
using ReactiveUI.Primitives.Disposables;

namespace Client.Views.Common;

/// <summary>
/// Runs the blocks of a view while it is loaded: each block subscribes when the view is loaded, and
/// whatever it adds to the disposable it is given ends when the view is unloaded or given another view model.
/// Stands in for WhenActivated of ReactiveUI, which looks the view up through reflection and so does not
/// survive trimming and NativeAOT.
/// </summary>
internal sealed class ViewActivation
{
    private readonly List<Action<MultipleDisposable>> _blocks = [];
    private MultipleDisposable? _subscriptions;

    public ViewActivation(Control view)
    {
        view.Loaded += (_, _) => Activate();
        view.Unloaded += (_, _) => Deactivate();
        view.DataContextChanged += (_, _) => Reactivate(view);

        if (view.IsLoaded)
            Activate();
    }

    public void Add(Action<MultipleDisposable> block)
    {
        _blocks.Add(block);

        if (_subscriptions != null)
            block(_subscriptions);
    }

    private void Activate()
    {
        if (_subscriptions != null)
            return;

        _subscriptions = new MultipleDisposable();
        foreach (var block in _blocks)
            block(_subscriptions);
    }

    // A recycled view stays loaded while it is given another view model, which the blocks subscribe to anew
    private void Reactivate(Control view)
    {
        if (!view.IsLoaded)
            return;

        Deactivate();

        if (view.DataContext != null)
            Activate();
    }

    private void Deactivate()
    {
        _subscriptions?.Dispose();
        _subscriptions = null;
    }
}
