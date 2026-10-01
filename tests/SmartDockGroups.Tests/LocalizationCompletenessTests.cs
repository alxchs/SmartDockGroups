using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace SmartDockGroups.Tests;

/// <summary>
/// Every language file carries every key (a missing one silently shows English, which is how
/// "Close group" and "Paste" stayed untranslated in six languages), with the same {0}/{1}
/// placeholders, and every key the code asks for exists.
/// </summary>
public sealed class LocalizationCompletenessTests
{
    private static readonly string[] Languages = ["en", "pt", "es", "de", "it", "pl", "ru", "ja"];

    private static string AppDirectory
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartDockGroups.slnx")))
            {
                directory = directory.Parent;
            }

            return Path.Combine(directory!.FullName, "src", "SmartDockGroups.App");
        }
    }

    private static Dictionary<string, string> Load(string language) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(AppDirectory, "Localization", $"Strings.{language}.json")))!;

    [Theory]
    [InlineData("pt")]
    [InlineData("es")]
    [InlineData("de")]
    [InlineData("it")]
    [InlineData("pl")]
    [InlineData("ru")]
    [InlineData("ja")]
    public void A_language_has_every_english_key_with_the_same_placeholders(string language)
    {
        var english = Load("en");
        var other = Load(language);

        Assert.Empty(english.Keys.Except(other.Keys));
        Assert.Empty(other.Keys.Except(english.Keys));

        static string Placeholders(string text) =>
            string.Join(",", Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).Distinct().OrderBy(x => x));

        var mismatched = english.Where(pair => Placeholders(pair.Value) != Placeholders(other[pair.Key])).Select(pair => pair.Key);
        Assert.Empty(mismatched);
    }

    [Fact]
    public void Every_key_the_code_asks_for_exists()
    {
        var english = Load("en");
        var sources = Directory.EnumerateFiles(AppDirectory, "*.*", SearchOption.AllDirectories)
            .Where(path => (path.EndsWith(".cs") || path.EndsWith(".xaml"))
                && !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));

        const string keyShape = @"(?:group|item|settings|tray|desktop|overlay|common)\.[A-Za-z]+";
        var pattern = new Regex("\"(" + keyShape + ")\"|Loc (" + keyShape + ")");
        var used = sources
            .SelectMany(path => pattern.Matches(File.ReadAllText(path))
                .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value))
            .Distinct();

        Assert.Empty(used.Where(key => !english.ContainsKey(key)));
    }
}
