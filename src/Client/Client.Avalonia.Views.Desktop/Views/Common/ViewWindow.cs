using System;
using Avalonia.Controls;
using ReactiveUI.Primitives.Disposables;

namespace Client.Views.Common;

/// <summary>
/// A window of one kind of view model, which is its data context.
/// </summary>
public class ViewWindow<TViewModel> : Window where TViewModel : class
{
    private ViewActivation? _activation;

    public TViewModel? ViewModel
    {
        get => DataContext as TViewModel;
        set => DataContext = value;
    }

    /// <summary>
    /// Runs <paramref name="block"/> every time the window is loaded; whatever the block adds to the
    /// disposable it is given ends when the window is unloaded.
    /// </summary>
    protected void WhenActivated(Action<MultipleDisposable> block)
    {
        _activation ??= new ViewActivation(this);
        _activation.Add(block);
    }
}
