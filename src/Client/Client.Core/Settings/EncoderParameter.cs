using System.Globalization;

namespace Client.Core.Settings;

public sealed record EncoderPropertyOverride(string Encoder, string Name, string Value);

/// <summary>A writable encoder property reported by the installed media runtime.</summary>
public sealed record EncoderParameter(string Name, string Description, string DefaultValue,
    string Kind, string? Minimum = null, string? Maximum = null, IReadOnlyList<string>? Choices = null)
{
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
