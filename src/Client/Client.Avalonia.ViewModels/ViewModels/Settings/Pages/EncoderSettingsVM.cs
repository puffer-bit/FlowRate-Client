using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Client.Avalonia.Localization;
using Client.Core.Settings;
using ReactiveUI;

namespace Client.ViewModels.Settings.Pages;

public sealed class EncoderParameterVM : ReactiveObject
{
    public EncoderParameter Parameter { get; }
    public string Name => Parameter.Name;
    public string Description => Parameter.Description;
    public string DefaultText => Strings.Format("settings.video.encoderSettings.default", ("value", Parameter.DefaultValue));
    public string RangeText => Parameter.Minimum == null ? "" : $"{Parameter.Minimum} … {Parameter.Maximum}";
    public IReadOnlyList<string> Choices => Parameter.Choices ?? [];
    public bool HasChoices => Choices.Count > 0;
    public bool IsOverridden
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(HasError));
        }
    }
    public string Value
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(HasError));
        }
    }
    public bool HasError => IsOverridden && !Parameter.IsValid(Value);

    public EncoderParameterVM(EncoderParameter parameter, string? value)
    {
        Parameter = parameter;
        Value = value ?? parameter.DefaultValue;
        IsOverridden = value != null;
    }
}

public sealed class EncoderSettingsVM : ReactiveObject
{
    public string Encoder { get; }
    public IReadOnlyList<EncoderParameterVM> Parameters { get; }
    public bool IsBusy { get; private set => this.RaiseAndSetIfChanged(ref field, value); }
    public string? Error { get; private set => this.RaiseAndSetIfChanged(ref field, value); }
    private readonly Func<IReadOnlyList<EncoderPropertyOverride>, Task> _save;

    public EncoderSettingsVM(string encoder, IReadOnlyList<EncoderParameter> parameters,
        IReadOnlyList<EncoderPropertyOverride> saved, Func<IReadOnlyList<EncoderPropertyOverride>, Task> save)
    {
        Encoder = encoder;
        Parameters = parameters.Select(p => new EncoderParameterVM(p,
            saved.FirstOrDefault(v => v.Encoder == encoder && v.Name == p.Name)?.Value)).ToArray();
        _save = save;
    }

    public void Reset()
    {
        foreach (var parameter in Parameters)
        {
            parameter.IsOverridden = false;
            parameter.Value = parameter.Parameter.DefaultValue;
        }
        Error = null;
    }

    public async Task<bool> SaveAsync()
    {
        if (IsBusy)
            return false;
        if (Parameters.Any(p => p.HasError))
        {
            Error = Strings.Get("settings.video.encoderSettings.invalid");
            return false;
        }
        IsBusy = true;
        Error = null;
        try
        {
            await _save(Parameters.Where(p => p.IsOverridden)
                .Select(p => new EncoderPropertyOverride(Encoder, p.Name, p.Value)).ToArray());
            return true;
        }
        catch (Exception)
        {
            Error = Strings.Get("settings.video.encoderSettings.saveFailed");
            return false;
        }
        finally { IsBusy = false; }
    }
}
