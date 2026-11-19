using System;
using System.Collections.Generic;
using System.Linq;
using Client.Core.Media;
using Client.Core.Settings;
using ReactiveUI;

namespace Client.ViewModels.Settings.Pages;

/// <summary>A saved codec preference and the installed element selected by automatic fallback.</summary>
public sealed class CodecSelectionVM : ReactiveObject
{
    private readonly IReadOnlyList<string> _available;
    private readonly Func<string, IReadOnlyList<CodecParameter>> _getParameters;

    private IReadOnlyList<CodecPropertyOverride> _overrides = [];

    public bool HasOverrides => _overrides.Any(p => p.Factory == EffectiveFactory);

    public void UpdateOverrides(IReadOnlyList<CodecPropertyOverride> overrides)
    {
        _overrides = overrides;
        this.RaisePropertyChanged(nameof(HasOverrides));
    }

    public VideoCodec Codec { get; }
    public string SettingKey { get; }
    public IReadOnlyList<string> Choices { get; }
    public int SelectedIndex
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(CanConfigure));
            this.RaisePropertyChanged(nameof(HasOverrides));
        }
    }

    public string? PreferredFactory => SelectedIndex > 0 && SelectedIndex <= _available.Count
        ? _available[SelectedIndex - 1] : null;
    public string? EffectiveFactory => PreferredFactory ?? _available.FirstOrDefault();
    public bool CanConfigure => MediaRuntime.IsEnabled && EffectiveFactory is { } factory
        && _getParameters(factory).Count > 0;

    public CodecSelectionVM(VideoCodec codec, bool isEncoder, IReadOnlyList<string> available,
        Func<string, IReadOnlyList<CodecParameter>> getParameters)
    {
        Codec = codec;
        _available = available;
        _getParameters = getParameters;
        var prefix = codec == VideoCodec.H264 ? "" : codec.ToString().ToLowerInvariant();
        var role = isEncoder ? "Encoder" : "Decoder";
        SettingKey = "video." + (prefix.Length == 0 ? role.ToLowerInvariant() : prefix + role);
        Choices = new string?[] { null }.Concat(available)
            .Select(factory => VideoControlVM.ChoiceText(factory, available)).ToArray();
    }

    public void Load(string? preferred, bool softwareOnly = false)
    {
        if (softwareOnly)
            preferred = _available.FirstOrDefault(VideoElementNames.IsSoftware);
        SelectedIndex = preferred == null ? 0 : Math.Max(0, _available.ToList().IndexOf(preferred) + 1);
    }
}
