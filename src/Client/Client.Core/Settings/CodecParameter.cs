using System.Globalization;

namespace Client.Core.Settings;

public sealed record CodecPropertyOverride(string Factory, string Name, string Value);

/// <summary>A writable encoder or decoder property reported by the installed media runtime.</summary>
public sealed record CodecParameter(string Name, string Description, string DefaultValue,
    string Kind, string? Minimum = null, string? Maximum = null, IReadOnlyList<string>? Choices = null)
{
    /// <summary>Valid overrides for one factory; the last saved value wins for duplicate names.</summary>
    public static IReadOnlyList<CodecPropertyOverride> ValidateOverrides(string factory,
        IReadOnlyList<CodecParameter> parameters, IReadOnlyList<CodecPropertyOverride> saved) =>
        saved.Where(value => value.Factory == factory)
            .GroupBy(value => value.Name, StringComparer.Ordinal)
            .Select(group => group.Last())
            .Where(value => parameters.Any(parameter => parameter.Name == value.Name && parameter.IsValid(value.Value)))
            .ToArray();

    public bool IsValid(string value)
    {
        if (Choices is { Count: > 0 })
            return Choices.Contains(value);
        if (Kind == "boolean")
            return value is "true" or "false";
        if (Kind == "integer")
            return decimal.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
                && number >= decimal.Parse(Minimum!, CultureInfo.InvariantCulture)
                && number <= decimal.Parse(Maximum!, CultureInfo.InvariantCulture);
        if (Kind == "number")
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                && double.IsFinite(number)
                && number >= double.Parse(Minimum!, CultureInfo.InvariantCulture)
                && number <= double.Parse(Maximum!, CultureInfo.InvariantCulture);
        return false;
    }
}
