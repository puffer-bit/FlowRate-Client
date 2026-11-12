using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Client.Avalonia.Localization;
using Client.Core.Settings;
using ReactiveUI;

namespace Client.ViewModels.Settings.Pages;

public sealed class CodecParameterVM : ReactiveObject
{
    public CodecParameter Parameter { get; }
    public string Name => Parameter.Name;
    public string Description => Parameter.Description;
    public string DefaultText => Strings.Format("settings.video.codecSettings.default", ("value", Parameter.DefaultValue));
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

    public CodecParameterVM(CodecParameter parameter, string? value)
    {
        Parameter = parameter;
        Value = value ?? parameter.DefaultValue;
        IsOverridden = value != null;
    }
}

public sealed class CodecSettingsVM : ReactiveObject
{
    public string Factory { get; }
    public IReadOnlyList<CodecParameterVM> Parameters { get; }
    public bool IsBusy { get; private set => this.RaiseAndSetIfChanged(ref field, value); }
    public string? Error { get; private set => this.RaiseAndSetIfChanged(ref field, value); }
    private readonly Func<IReadOnlyList<CodecPropertyOverride>, Task> _save;

    public CodecSettingsVM(string factory, IReadOnlyList<CodecParameter> parameters,
        IReadOnlyList<CodecPropertyOverride> saved, Func<IReadOnlyList<CodecPropertyOverride>, Task> save)
    {
        Factory = factory;
        Parameters = parameters.Select(p => new CodecParameterVM(p,
            saved.LastOrDefault(v => v.Factory == factory && v.Name == p.Name)?.Value)).ToArray();
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
            Error = Strings.Get("settings.video.codecSettings.invalid");
            return false;
        }
        IsBusy = true;
        Error = null;
        try
        {
            await _save(Parameters.Where(p => p.IsOverridden)
                .Select(p => new CodecPropertyOverride(Factory, p.Name, p.Value)).ToArray());
            return true;
        }
        catch (Exception)
        {
            Error = Strings.Get("settings.video.codecSettings.saveFailed");
            return false;
        }
        finally { IsBusy = false; }
    }
}
