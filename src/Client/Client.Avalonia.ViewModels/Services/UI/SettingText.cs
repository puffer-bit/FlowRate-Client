using System.Collections.Generic;
using Avalonia;
using Client.Application.Settings.Search;
using Client.Avalonia.Controls;
using Client.Avalonia.Controls.Settings;
using Client.Avalonia.Localization;

namespace Client.Services;

/// <summary>
/// Ties a setting to its texts by one key, "tab.setting": the texts are
/// <c>settings.{key}.title</c> and <c>settings.{key}.description</c> of <see cref="Strings"/>.
/// A view sets <see cref="KeyProperty"/> on a <see cref="SettingsRow"/> and a view model lists the
/// same key in its search entries, so the row and the search result cannot show different texts.
/// </summary>
public static class SettingText
{
    public static readonly AttachedProperty<string?> KeyProperty =
        AvaloniaProperty.RegisterAttached<SettingsRow, SettingsRow, string?>("Key");

    /// <summary>
    /// Names the section a <see cref="SettingsHeader"/> opens, "tab.section": its text is
    /// <c>settings.{tab}.section.{section}</c> and the same key remembers whether it is collapsed.
    /// </summary>
    public static readonly AttachedProperty<string?> SectionProperty =
        AvaloniaProperty.RegisterAttached<SettingsHeader, SettingsHeader, string?>("Section");

    static SettingText()
    {
        SectionProperty.Changed.AddClassHandler<SettingsHeader>((header, change) =>
        {
            if (change.NewValue is not string key || key.Split('.') is not [var tab, var section])
                return;

            header.Bind(SettingsHeader.TextProperty, Strings.Observe($"settings.{tab}.section.{section}"));
            header.SectionKey = key;
        });

        KeyProperty.Changed.AddClassHandler<SettingsRow>((row, change) =>
        {
            if (change.NewValue is not string key)
                return;

            // Bound and not assigned, so that the row follows a change of the language
            row.Bind(SettingsRow.TitleProperty, Strings.Observe($"settings.{key}.title"));
            row.Bind(SettingsRow.DescriptionProperty, Strings.ObserveOptional($"settings.{key}.description"));
            SettingsPage.SetSearchKey(row, key);
        });
    }

    public static string? GetKey(SettingsRow row) => row.GetValue(KeyProperty);

    public static void SetKey(SettingsRow row, string? value) => row.SetValue(KeyProperty, value);

    public static string? GetSection(SettingsHeader header) => header.GetValue(SectionProperty);

    public static void SetSection(SettingsHeader header, string? value) => header.SetValue(SectionProperty, value);

    public static string TabName(string tab) => Strings.Get($"settings.{tab}.name");

    /// <summary>
    /// The search entries of a tab: its settings, section by section, in the order they are shown.
    /// The texts are the ones of the language at the moment of the call.
    /// </summary>
    /// <param name="tab">Key of the tab, the first part of the setting keys.</param>
    /// <param name="sections">Key of each section and the keys of its settings, without the tab.</param>
    public static IReadOnlyList<SettingSearchEntry> Entries(string tab, params (string Section, string[] Settings)[] sections)
    {
        var entries = new List<SettingSearchEntry>();
        foreach (var (section, settings) in sections)
        {
            var sectionKey = $"settings.{tab}.section.{section}";
            foreach (var setting in settings)
            {
                var key = $"{tab}.{setting}";
                var text = new SettingSearchText(Title(key), Description(key), Strings.Get(sectionKey));

                // English is matched as well once the interface is in another language
                var invariant = Strings.Current == Strings.Invariant
                    ? null
                    : new SettingSearchText(
                        Strings.FindInvariant($"settings.{key}.title") ?? text.Title,
                        Strings.FindInvariant($"settings.{key}.description"),
                        Strings.FindInvariant(sectionKey) ?? text.Section);

                entries.Add(new SettingSearchEntry(key, text, invariant));
            }
        }

        return entries;
    }

    private static string Title(string key) => Strings.Get($"settings.{key}.title");

    private static string? Description(string key) => Strings.Find($"settings.{key}.description");
}
