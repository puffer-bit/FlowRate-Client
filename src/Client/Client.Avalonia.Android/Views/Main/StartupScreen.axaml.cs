using System;
using System.Threading.Tasks;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Client.Avalonia.Styles.Animation;
using Client.ViewModels.Main;

namespace Client.Droid.Views.Main;

internal partial class StartupScreen : UserControl
{
    public StartupScreen()
    {
        InitializeComponent();
    }

    public async Task FadeOutAsync()
    {
        var duration = Motion.Get(MotionDuration.Startup);
        if (duration <= TimeSpan.Zero)
            return;

        await new global::Avalonia.Animation.Animation
        {
            Duration = duration,
            Easing = Motion.Ease(MotionEase.Standard),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 1d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 0d) } }
            }
        }.RunAsync(this);
    }

    private void OnContinueClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowVM viewModel)
            viewModel.ContinueStartup();
    }

    private void OnExitClick(object? sender, RoutedEventArgs e) => MainActivity.Current?.FinishAndRemoveTask();
}
