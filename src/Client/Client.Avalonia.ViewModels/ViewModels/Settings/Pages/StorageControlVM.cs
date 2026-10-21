using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Client.Application.Settings;
using Client.Application.Settings.Search;
using Client.Avalonia.Localization;
using Client.Core.Settings;
using Client.Services;
using Client.ViewModels.Dialogs;
using ReactiveUI;
using Result = Client.ViewModels.Dialogs.Result;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Settings.Pages;

/// <summary>
/// What of the chats is kept on this device: the messages and the images, how much of them,
/// and how much they take now.
/// </summary>
public class StorageControlVM : ReactiveObject, ISettingVM
{
    private const long Megabyte = 1024 * 1024;

    public string Name => SettingText.TabName("storage");
    public SettingTabType Type => SettingTabType.Storage;
    public bool IsEnabled => true;

    public IReadOnlyList<SettingSearchEntry> SearchEntries => SettingText.Entries("storage",
        ("messages", ["keepMessages", "messagesPerChat", "clearMessages"]),
        ("media", ["mediaLimit", "clearMedia"]));

    public bool IsChatCacheEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    // Index into ChatCacheMessageChoices
    public int ChatCacheMessagesIndex
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    // Index into MediaCacheLimitChoices
    public int MediaCacheLimitIndex
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public IReadOnlyList<string> ChatCacheMessageChoices { get; } = StorageSettings.ChatCacheMessageChoices
        .Select(count => count.ToString(CultureInfo.InvariantCulture))
        .ToList();

    public IReadOnlyList<string> MediaCacheLimitChoices { get; } = StorageSettings.MediaCacheLimitChoices
        .Select(megabytes => SizeText.Format(megabytes * Megabyte))
        .ToList();

    /// <summary>How many messages are kept and what they take.</summary>
    public string? ChatCacheUsageText
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>What the kept images take of the space they are given.</summary>
    public string? MediaCacheUsageText
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool HasChatCache
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool HasMediaCache
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> ClearChatCacheCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ClearMediaCacheCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; init; }

    /// <summary>Asks the user before what is kept is removed.</summary>
    public Interaction<MessageBoxVM, Result> ShowMessageBox { get; } = new();

    private readonly IStorageService _storageService;

    // Set while the view model fills its own properties, so that only user changes get saved
    private bool _isUpdating = true;

    public StorageControlVM(IStorageService storageService)
    {
        _storageService = storageService;

        LoadCommand = ReactiveCommand.CreateFromTask(LoadAsync);
        ClearChatCacheCommand = ReactiveCommand.CreateFromTask(
            () => ClearAsync("clearMessages", _storageService.ClearChatCacheAsync),
            this.WatchProperty(nameof(HasChatCache), vm => vm.HasChatCache));
        ClearMediaCacheCommand = ReactiveCommand.CreateFromTask(
            () => ClearAsync("clearMedia", _storageService.ClearMediaCacheAsync),
            this.WatchProperty(nameof(HasMediaCache), vm => vm.HasMediaCache));

        // Every change saves the whole tab, one save at a time
        this.WatchProperties(
                nameof(IsChatCacheEnabled),
                nameof(ChatCacheMessagesIndex),
                nameof(MediaCacheLimitIndex))
            .Where(_ => !_isUpdating)
            .Select(_ => Observable.FromAsync(SaveAsync))
            .Concat()
            .Subscribe();
    }

    /// <summary>
    /// Clears after the user has agreed: what is removed comes from the server again, but not at once
    /// and not without a connection.
    /// </summary>
    private async Task ClearAsync(string setting, Func<Task> clear)
    {
        var answer = await ShowMessageBox.Handle(new MessageBoxVM(Icon.Question, Buttons.YesNo,
            Strings.Get($"settings.storage.{setting}.question"),
            Strings.Get($"settings.storage.{setting}.title")));

        if (answer != Result.Yes)
            return;

        await clear();
        await RefreshUsageAsync();
    }

    private async Task LoadAsync()
    {
        _isUpdating = true;
        Apply(await _storageService.GetSettingsAsync());
        _isUpdating = false;

        await RefreshUsageAsync();
    }

    private async Task SaveAsync()
    {
        await _storageService.SaveSettingsAsync(ToSettings());

        // Turning the keeping off, or giving the images less room, removes what was kept
        await RefreshUsageAsync();
    }

    private async Task RefreshUsageAsync()
    {
        var usage = await _storageService.GetUsageAsync();

        HasChatCache = usage.ChatMessages > 0;
        HasMediaCache = usage.MediaBytes > 0;
        ChatCacheUsageText = Strings.Format("settings.storage.messagesUsage",
            ("count", usage.ChatMessages), ("size", SizeText.Format(usage.ChatBytes)));
        MediaCacheUsageText = Strings.Format("settings.storage.mediaUsage",
            ("size", SizeText.Format(usage.MediaBytes)),
            ("limit", SizeText.Format(StorageSettings.MediaCacheLimitChoices[MediaCacheLimitIndex] * Megabyte)));
    }

    private StorageSettings ToSettings() => new()
    {
        IsChatCacheEnabled = IsChatCacheEnabled,
        ChatCacheMessagesPerChat = StorageSettings.ChatCacheMessageChoices[ChatCacheMessagesIndex],
        MediaCacheLimitMegabytes = StorageSettings.MediaCacheLimitChoices[MediaCacheLimitIndex]
    };

    private void Apply(StorageSettings settings)
    {
        var defaults = new StorageSettings();

        IsChatCacheEnabled = settings.IsChatCacheEnabled;
        ChatCacheMessagesIndex = IndexOf(StorageSettings.ChatCacheMessageChoices,
            settings.ChatCacheMessagesPerChat, defaults.ChatCacheMessagesPerChat);
        MediaCacheLimitIndex = IndexOf(StorageSettings.MediaCacheLimitChoices,
            settings.MediaCacheLimitMegabytes, defaults.MediaCacheLimitMegabytes);
    }

    // A saved value that is not among the choices anymore reads as the default one
    private static int IndexOf(IReadOnlyList<int> choices, int value, int fallback)
    {
        for (var i = 0; i < choices.Count; i++)
        {
            if (choices[i] == value)
                return i;
        }

        return Math.Max(0, choices.ToList().IndexOf(fallback));
    }

    /// <summary>
    /// The defaults of the tab. What is kept is data and stays, unless the defaults leave it less room.
    /// </summary>
    public async Task ResetAllAsync()
    {
        var defaults = new StorageSettings();

        _isUpdating = true;
        Apply(defaults);
        _isUpdating = false;

        await _storageService.SaveSettingsAsync(defaults);
        await RefreshUsageAsync();
    }
}
