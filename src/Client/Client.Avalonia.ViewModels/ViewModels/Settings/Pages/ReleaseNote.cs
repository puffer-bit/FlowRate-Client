using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Client.Application.Updates;

namespace Client.ViewModels.Settings.Pages;

public sealed class ReleaseNote(PublishedRelease release)
{
    // A collapsed release names this many of its sections; the rest are seen when it is expanded
    private const int MaxHighlights = 4;

    public string Version => release.Tag;
    public string Title => release.Name;
    public string Date => release.PublishedAt.UtcDateTime.ToString("yyyy-MM-dd");
    public Uri Url => release.Url;
    public bool IsLatest { get; init; }

    // Kept by the view, so a release stays open while more are shown below it
    public bool IsExpanded { get; set; }

    public IReadOnlyList<ReleaseNoteSection> Sections { get; } = Parse(release.Markdown);

    /// <summary>The listed changes of all the sections.</summary>
    public int ChangeCount => Sections.Sum(section => section.ChangeCount);

    public bool HasChanges => ChangeCount > 0;

    /// <summary>The sections a collapsed release shows as chips: those with a heading and changes in them.</summary>
    public IReadOnlyList<ReleaseNoteSection> Highlights => field ??=
        Sections.Where(section => section.HasHeading && section.HasChanges).Take(MaxHighlights).ToArray();

    public bool HasHighlights => Highlights.Count > 0;

    private static IReadOnlyList<ReleaseNoteSection> Parse(string markdown)
    {
        var sections = new List<ReleaseNoteSection>();
        var heading = "";
        var lines = new List<string>();
        foreach (var raw in markdown.TrimStart('\uFEFF').Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith('#'))
            {
                Flush();
                heading = PlainText(line.TrimStart('#', ' '));
            }
            else
            {
                lines.Add(line);
            }
        }
        Flush();
        return sections;

        void Flush()
        {
            var body = PlainText(string.Join('\n', lines).Trim());
            body = Regex.Replace(body, @"(?m)^[-*] ", "• ");
            if (heading.Length > 0 || body.Length > 0)
                sections.Add(new ReleaseNoteSection(heading, body, string.Join('\n', lines).Trim()));
            lines.Clear();
        }
    }

    private static string PlainText(string text) => Regex.Replace(text, @"\[([^\]]+)\]\(([^)]+)\)", "$1 ($2)")
        .Replace("**", "", StringComparison.Ordinal).Replace("`", "", StringComparison.Ordinal);
}

public sealed record ReleaseNoteSection(string Heading, string Body, string Markdown)
{
    public IReadOnlyList<ReleaseNoteBlock> Blocks { get; } = ParseBlocks(Markdown);
    public string IconKey => Heading.ToLowerInvariant() switch
    {
        var h when Contains(h, "fix", "bug", "исправ", "ошиб") => "GearIcon",
        var h when Contains(h, "feature", "added", "new", "нов", "добав") => "SparklesIcon",
        var h when Contains(h, "improv", "performance", "улучш", "оптим") => "StatsIcon",
        var h when Contains(h, "build", "deploy", "сбор", "зависим", "depend") => "PlugIcon",
        var h when Contains(h, "security", "безопас") => "KeyIcon",
        var h when Contains(h, "link", "ссыл") => "PlanetIcon",
        _ => "LogsIcon"
    };

    private static bool Contains(string heading, params string[] words) => words.Any(heading.Contains);

    private static IReadOnlyList<ReleaseNoteBlock> ParseBlocks(string markdown)
    {
        var blocks = new List<ReleaseNoteBlock>();
        var paragraph = new List<string>();
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.Trim();
            var list = Regex.Match(line, @"^(?:[-*+] |(\d+)\. )(.*)$");
            if (line.Length == 0 || list.Success)
            {
                Flush();
                if (list.Success)
                    blocks.Add(new ReleaseNoteBlock(list.Groups[1].Success ? list.Groups[1].Value + "." : "•", list.Groups[2].Value));
            }
            else if (paragraph.Count == 0 && blocks.Count > 0 && raw.StartsWith("  ", StringComparison.Ordinal))
            {
                blocks[^1] = blocks[^1] with { Markdown = blocks[^1].Markdown + " " + line };
            }
            else paragraph.Add(line);
        }
        Flush();
        return blocks;

        void Flush()
        {
            if (paragraph.Count > 0) blocks.Add(new ReleaseNoteBlock("", string.Join(" ", paragraph)));
            paragraph.Clear();
        }
    }

    public int ChangeCount => Blocks.Count(block => block.IsListItem);
    public bool HasChanges => ChangeCount > 0;
    public bool HasHeading => Heading.Length > 0;
    public bool HasBody => Body.Length > 0;
}

public sealed record ReleaseNoteBlock(string Marker, string Markdown)
{
    public bool IsListItem => Marker.Length > 0;
}
