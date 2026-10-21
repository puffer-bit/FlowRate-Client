using System.Text.RegularExpressions;
using Xunit;

namespace Client.Avalonia.Localization.Tests;

/// <summary>
/// Reads the sources of the interface: every key they name has a text, and markup holds no text of its own.
/// </summary>
public partial class SourceTextTests
{
    // Texts that are the same in every language
    private static readonly string[] AllowedLiterals =
    [
        "Client.Avalonia.Desktop", "TestServer", "Welcome to Avalonia!", "ID: {0}"
    ];

    // Elements whose content is a value and not a text
    private static readonly string[] ValueElements = ["Color"];

    private static readonly string[] InterfaceProjects =
        ["Client.Avalonia.Desktop", "Client.Avalonia.Controls", "Client.Avalonia.Styles"];

    private static readonly StringTable English = Strings.Invariant;

    [Fact]
    public void KeysNamedInMarkupExist()
    {
        var missing = new List<string>();

        foreach (var file in SourceFiles("*.axaml"))
        {
            var text = File.ReadAllText(file);

            foreach (Match match in MarkupKeyPattern().Matches(text))
                Check(match.Groups[1].Value, file, missing);

            foreach (Match match in SettingKeyPattern().Matches(text))
                Check($"settings.{match.Groups[1].Value}.title", file, missing);
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void KeysNamedInCodeExist()
    {
        var missing = new List<string>();

        foreach (var file in SourceFiles("*.cs"))
        {
            var text = File.ReadAllText(file);

            foreach (Match match in CodeKeyPattern().Matches(text))
                Check(match.Groups[1].Value, file, missing);

        }

        Assert.Empty(missing);
    }

    [Fact]
    public void FormatCallsGiveTheArgumentsTheTextNames()
    {
        var different = new List<string>();

        foreach (var file in SourceFiles("*.cs"))
        {
            var text = File.ReadAllText(file);

            foreach (Match match in FormatCallPattern().Matches(text))
            {
                var key = match.Groups[1].Value;
                if (English.Find(key) is not { } message)
                    continue;

                var given = ArgumentNamePattern().Matches(CallArguments(text, match.Index + match.Length))
                    .Select(argument => argument.Groups[1].Value)
                    .ToHashSet();

                if (!given.SetEquals(StringTableTests.Placeholders(message)))
                    different.Add($"{Path.GetFileName(file)}: {key}");
            }
        }

        Assert.Empty(different);
    }

    // From after the key to the parenthesis that closes the call
    private static string CallArguments(string text, int start)
    {
        var depth = 1;
        var end = start;

        for (; end < text.Length && depth > 0; end++)
        {
            if (text[end] == '(')
                depth++;
            else if (text[end] == ')')
                depth--;
        }

        return text[start..end];
    }

    [Fact]
    public void MarkupHoldsNoTextOfItsOwn()
    {
        var literals = new List<string>();

        foreach (var file in SourceFiles("*.axaml"))
        {
            // What a designer previews is not shown to the user
            var text = PreviewPattern().Replace(File.ReadAllText(file), string.Empty);
            text = CommentPattern().Replace(text, string.Empty);

            var found = AttributeTextPattern().Matches(text).Select(match => match.Groups[1].Value)
                .Concat(BindingTextPattern().Matches(text).Select(match => match.Groups[1].Value))
                .Concat(ElementTextPattern().Matches(text)
                    .Where(match => !ValueElements.Contains(match.Groups[2].Value))
                    .Select(match => match.Groups[1].Value.Trim()));

            foreach (var literal in found)
            {
                if (WordPattern().IsMatch(literal) && !AllowedLiterals.Contains(literal))
                    literals.Add($"{Path.GetFileName(file)}: {literal}");
            }
        }

        Assert.Empty(literals);
    }

    private static void Check(string key, string file, List<string> missing)
    {
        if (English.Find(key) == null)
            missing.Add($"{Path.GetFileName(file)}: {key}");
    }

    private static IEnumerable<string> SourceFiles(string pattern)
    {
        var root = FindSourceRoot();

        return InterfaceProjects
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(root, project), pattern, SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                           && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
    }

    private static string FindSourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Client.Avalonia.Desktop")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("The tests read the sources and have to run from the repository");
    }

    [GeneratedRegex(@"\{l:Text\s+(?:Key=)?([A-Za-z0-9_.]+)\}")]
    private static partial Regex MarkupKeyPattern();

    [GeneratedRegex(@"SettingText\.Key=""([^""]+)""")]
    private static partial Regex SettingKeyPattern();

    // A key put together at run time has no closing quote right after it and is skipped
    [GeneratedRegex("""Strings\.(?:Get|Find|Format|Observe|ObserveOptional)\(\s*"([A-Za-z0-9_.]+)"\s*[,)]""")]
    private static partial Regex CodeKeyPattern();

    [GeneratedRegex("""Strings\.Format\(\s*"([A-Za-z0-9_.]+)"\s*,""")]
    private static partial Regex FormatCallPattern();

    [GeneratedRegex("""\(\s*"([A-Za-z_][A-Za-z0-9_]*)"\s*,""")]
    private static partial Regex ArgumentNamePattern();

    [GeneratedRegex(@"<Design\.PreviewWith>.*?</Design\.PreviewWith>", RegexOptions.Singleline)]
    private static partial Regex PreviewPattern();

    [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex CommentPattern();

    [GeneratedRegex(@"\s(?:Text|Content|Header|Watermark|PlaceholderText|Title|Description|ToolTip\.Tip)=""([^{""][^""]*)""")]
    private static partial Regex AttributeTextPattern();

    // Texts inside a binding: a fallback, a value for null or a format with words
    [GeneratedRegex("(?:TargetNullValue|FallbackValue|StringFormat)='([^']*)'")]
    private static partial Regex BindingTextPattern();

    [GeneratedRegex(@">([^<>{}]+)</([\w:.]+)>")]
    private static partial Regex ElementTextPattern();

    // Two letters in a row: signs and units such as "+" or "%" are no text
    [GeneratedRegex(@"\p{L}{2}")]
    private static partial Regex WordPattern();
}
