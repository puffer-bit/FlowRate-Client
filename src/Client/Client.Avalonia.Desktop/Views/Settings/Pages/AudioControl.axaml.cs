using System;
using System.Collections.Specialized;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Client.Avalonia.Controls.Settings;
using Client.ViewModels.Settings.Pages;
using Client.Views.Common;

namespace Client.Views.Settings.Pages;

public partial class AudioControl : ViewControl<AudioControlVM>
{
    // Remembered with the collapsed sections of the settings, where a key that is listed is folded
    private const string SoundListKey = "audio.soundEvents";

    private INotifyCollectionChanged? _collapsedSections;

    // Set while the arrow is moved to what is remembered, which is not a change to remember
    private bool _isLoadingSoundList;

    public AudioControl()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // The settings may still be reading the list when the page is shown
        _collapsedSections = SettingsPage.GetCollapsedSections(this) as INotifyCollectionChanged;
        if (_collapsedSections != null)
            _collapsedSections.CollectionChanged += OnCollapsedSectionsChanged;

        LoadSoundListState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_collapsedSections != null)
            _collapsedSections.CollectionChanged -= OnCollapsedSectionsChanged;
        _collapsedSections = null;

        base.OnDetachedFromVisualTree(e);
    }

    private void OnCollapsedSectionsChanged(object? sender, NotifyCollectionChangedEventArgs e) => LoadSoundListState();

    private void LoadSoundListState()
    {
        if (SettingsPage.GetCollapsedSections(this) is not { } collapsed)
            return;

        _isLoadingSoundList = true;
        SoundListToggle.IsChecked = !collapsed.Contains(SoundListKey);
        _isLoadingSoundList = false;
    }

    private void OnSoundListToggled(object? sender, RoutedEventArgs e)
    {
        var isExpanded = SoundListToggle.IsChecked == true;
        SoundListRow.IsExpanded = isExpanded;

        if (_isLoadingSoundList || SettingsPage.GetCollapsedSections(this) is not { } collapsed)
            return;

        if (isExpanded)
            collapsed.Remove(SoundListKey);
        else if (!collapsed.Contains(SoundListKey))
            collapsed.Add(SoundListKey);
    }

    // The packs are folders the user copies in by hand, so the list is read again when it is opened
    private void OnSoundPacksOpened(object? sender, EventArgs e)
    {
        (DataContext as AudioControlVM)?.RefreshSoundPacks();
    }

    private async void OnOpenSoundPacksFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AudioControlVM viewModel || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        try
        {
            var directory = Directory.CreateDirectory(viewModel.SoundPacksDirectory);
            await topLevel.Launcher.LaunchDirectoryInfoAsync(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Nothing to open: the folder cannot be created here
        }
    }
}
