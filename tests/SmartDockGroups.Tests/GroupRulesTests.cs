using System.IO;
using SmartDockGroups.App.Services;
using SmartDockGroups.App.Settings;
using SmartDockGroups.Core.Configuration;
using SmartDockGroups.Core.Models;
using Xunit;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;

namespace SmartDockGroups.Tests;

/// <summary>
/// The rules added in the 2026-09-30 batch: unique names inside a group, sharing one
/// aspect of a group's look at a time, new groups starting from chosen defaults, the
/// prompt opening next to the pointer, and recovering a shortcut whose file was deleted.
/// </summary>
public sealed class GroupRulesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SmartDockGroupsTests", Guid.NewGuid().ToString("N"));

    public GroupRulesTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static LaunchItem Item(string name, string target = "C:\\x.exe") => new()
    {
        Name = name,
        Type = LaunchItemType.Application,
        Target = target,
        IsDesktopPinned = true
    };

    // ── Unique names

    [Fact]
    public void A_name_is_taken_regardless_of_case_and_surrounding_spaces()
    {
        var group = new MenuCategory { Name = "G", Items = [Item("DBeaver")] };

        Assert.True(GroupNames.IsTaken(group, "dbeaver"));
        Assert.True(GroupNames.IsTaken(group, "  DBEAVER "));
        Assert.False(GroupNames.IsTaken(group, "DBeaver 2"));
    }

    [Fact]
    public void Renaming_an_item_to_its_own_name_is_not_a_clash()
    {
        var item = Item("Edge");
        var group = new MenuCategory { Name = "G", Items = [item] };

        Assert.False(GroupNames.IsTaken(group, "Edge", except: item));
    }

    [Fact]
    public void A_repeated_name_gets_the_first_free_numbered_suffix()
    {
        var group = new MenuCategory { Name = "G", Items = [Item("Edge"), Item("Edge (2)")] };

        Assert.Equal("Edge (3)", GroupNames.MakeUnique(group, "Edge"));
        Assert.Equal("Chrome", GroupNames.MakeUnique(group, "Chrome"));
    }

    [Fact]
    public void Folders_and_items_share_one_namespace()
    {
        var group = new MenuCategory { Name = "G", Categories = [new MenuCategory { Name = "Tools" }] };

        Assert.True(GroupNames.IsTaken(group, "tools"));
    }

    [Fact]
    public void The_same_shortcut_twice_is_recognised_so_it_is_not_added_again()
    {
        var existing = Item("Edge", "C:\\S\\Edge.lnk");
        var group = new MenuCategory { Name = "G", Items = [existing] };

        Assert.Same(existing, GroupNames.FindSameShortcut(group, Item("edge", "c:\\s\\edge.lnk")));
        Assert.Null(GroupNames.FindSameShortcut(group, Item("Edge", "C:\\Other\\Edge.lnk")));
    }

    [Fact]
    public void Repairing_an_old_configuration_keeps_the_first_name_and_numbers_the_repeats()
    {
        var first = Item("App");
        var second = Item("app");
        var third = Item("App");
        var group = new MenuCategory { Name = "G", IsDesktopGroup = true, Items = [first, second, third] };
        var configuration = new LauncherConfiguration { Categories = [group] };

        Assert.True(GroupNames.EnsureUnique(configuration));
        Assert.Equal("App", first.Name);
        Assert.Equal("app (2)", second.Name);
        Assert.Equal("App (3)", third.Name);
        Assert.False(GroupNames.EnsureUnique(configuration));
    }

    // ── Sharing one aspect at a time

    private static (MenuCategory Source, MenuCategory Target, MenuTheme Default) TwoGroups()
    {
        var defaultTheme = new MenuTheme { BackgroundColor = "#111111", TextColor = "#FFFFFF" };
        var source = new MenuCategory
        {
            Name = "Source",
            ThemeOverride = new MenuTheme { BackgroundColor = "#224466", TextColor = "#EEEEEE", HighlightColor = "#55AA88" },
            DesktopBackgroundImagePath = "C:\\img\\sea.png",
            AreaOpacity = 0.4,
            TitleOpacity = 0.6,
            IconHGap = 25,
            IconVGap = 28,
            DesktopIconScale = 1.5
        };
        var target = new MenuCategory { Name = "Target", DesktopBackgroundImagePath = "C:\\img\\own.png" };
        return (source, target, defaultTheme);
    }

    [Fact]
    public void Sharing_only_the_image_leaves_the_colour_alone()
    {
        var (source, target, defaultTheme) = TwoGroups();

        target.CopyVisualFrom(source, VisualAspects.BackgroundImage, defaultTheme);

        Assert.Equal("C:\\img\\sea.png", target.DesktopBackgroundImagePath);
        Assert.Null(target.ThemeOverride);
        Assert.Equal(1.0, target.AreaOpacity);
        Assert.Equal(10, target.IconHGap);
    }

    [Fact]
    public void Sharing_only_the_colour_leaves_the_image_and_the_rest_of_the_theme_alone()
    {
        var (source, target, defaultTheme) = TwoGroups();

        target.CopyVisualFrom(source, VisualAspects.BackgroundColor, defaultTheme);

        Assert.Equal("C:\\img\\own.png", target.DesktopBackgroundImagePath);
        Assert.Equal("#224466", target.ThemeOverride!.BackgroundColor);
        Assert.Equal("#EEEEEE", target.ThemeOverride.TextColor);
        Assert.Equal(defaultTheme.HighlightColor, target.ThemeOverride.HighlightColor);
    }

    [Fact]
    public void Sharing_only_the_spacing_moves_both_gaps_and_nothing_else()
    {
        var (source, target, defaultTheme) = TwoGroups();

        target.CopyVisualFrom(source, VisualAspects.IconSpacing, defaultTheme);

        Assert.Equal((25, 28), (target.IconHGap, target.IconVGap));
        Assert.Equal(1.0, target.DesktopIconScale);
        Assert.Equal("C:\\img\\own.png", target.DesktopBackgroundImagePath);
    }

    [Fact]
    public void Sharing_everything_matches_the_old_all_or_nothing_copy()
    {
        var (source, target, defaultTheme) = TwoGroups();

        target.CopyVisualFrom(source, VisualAspects.All, defaultTheme);

        Assert.Equal("#55AA88", target.ThemeOverride!.HighlightColor);
        Assert.Equal("C:\\img\\sea.png", target.DesktopBackgroundImagePath);
        Assert.Equal((0.4, 0.6), (target.AreaOpacity, target.TitleOpacity));
        Assert.Equal((25, 28, 1.5), (target.IconHGap, target.IconVGap, target.DesktopIconScale));
    }

    [Fact]
    public void Defaults_apply_only_the_aspects_that_were_chosen()
    {
        var (source, _, _) = TwoGroups();
        var defaults = new GroupDefaults();
        defaults.TakeFrom(source, VisualAspects.Opacity | VisualAspects.IconSize);

        var fresh = new MenuCategory { Name = "New" };
        defaults.ApplyTo(fresh);

        Assert.Equal((0.4, 0.6, 1.5), (fresh.AreaOpacity, fresh.TitleOpacity, fresh.DesktopIconScale));
        Assert.Null(fresh.DesktopBackgroundImagePath);
        Assert.Equal((10, 10), (fresh.IconHGap, fresh.IconVGap));
    }

    [Fact]
    public void Group_defaults_survive_a_save_and_reload()
    {
        var path = Path.Combine(_directory, "config.json");
        var configuration = new LauncherConfiguration();
        configuration.GroupDefaults.IconHGap = 22;
        configuration.GroupDefaults.BackgroundImagePath = "C:\\img\\sea.png";

        new ConfigurationStore(path).Save(configuration);
        Assert.True(new ConfigurationStore(path).TryLoad(out var reloaded));

        Assert.Equal(22, reloaded.GroupDefaults.IconHGap);
        Assert.Equal("C:\\img\\sea.png", reloaded.GroupDefaults.BackgroundImagePath);
        Assert.Null(reloaded.GroupDefaults.AreaOpacity);
    }

    // ── Prompt next to the pointer

    [Fact]
    public void The_prompt_opens_centred_under_the_pointer()
    {
        var area = new Rect(0, 0, 3840, 2160);
        var position = PromptPositioning.CalculateNearCursorPosition(area, new Point(3000, 400), 400, 160, gap: 12);

        Assert.Equal(new Point(2800, 412), position);
    }

    [Fact]
    public void Near_an_edge_the_prompt_is_pushed_back_inside_the_work_area()
    {
        var area = new Rect(1920, 40, 1920, 1040);
        var position = PromptPositioning.CalculateNearCursorPosition(area, new Point(3830, 1070), 400, 160, gap: 12);

        Assert.Equal(new Point(3840 - 400, 1080 - 160), position);
    }

    // ── Shortcut recovery

    private string Shortcut(string relativePath)
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "[InternetShortcut]\r\nURL=https://example.com/" + Path.GetFileNameWithoutExtension(path).Replace(' ', '-') + "\r\n");
        return path;
    }

    [Fact]
    public void A_lost_shortcut_is_found_by_its_file_name_in_another_folder()
    {
        var found = Shortcut("Programs\\Tools\\WinDirStat.url");
        Shortcut("Programs\\Tools\\Uninstall WinDirStat.url");

        var result = ShortcutStore.FindReplacement("C:\\gone\\TaskBar\\WinDirStat.url", "WinDirStat", [_directory]);

        Assert.Equal(found, result);
    }

    [Fact]
    public void A_shorter_name_that_prefixes_the_lost_one_is_accepted_when_it_is_the_only_one()
    {
        var found = Shortcut("Programs\\DBeaver.url");

        var result = ShortcutStore.FindReplacement("C:\\gone\\DBeaver Community.url", "DBeaver Community", [_directory]);

        Assert.Equal(found, result);
    }

    [Fact]
    public void Two_copies_of_the_same_shortcut_are_not_ambiguous()
    {
        var a = Shortcut("User\\Edge.url");
        var b = Shortcut("Common\\Edge.url");

        var result = ShortcutStore.FindReplacement("C:\\gone\\Edge.url", "Edge", [_directory]);

        Assert.Contains(result, new[] { a, b });
    }

    [Fact]
    public void Two_different_shortcuts_with_the_same_name_are_never_guessed_between()
    {
        var a = Path.Combine(_directory, "One", "Tool.url");
        var b = Path.Combine(_directory, "Two", "Tool.url");
        Directory.CreateDirectory(Path.GetDirectoryName(a)!);
        Directory.CreateDirectory(Path.GetDirectoryName(b)!);
        File.WriteAllText(a, "[InternetShortcut]\r\nURL=https://one.example\r\n");
        File.WriteAllText(b, "[InternetShortcut]\r\nURL=https://two.example\r\n");

        Assert.Null(ShortcutStore.FindReplacement("C:\\gone\\Tool.url", "Tool", [_directory]));
    }

    [Fact]
    public void Nothing_similar_means_nothing_is_recovered()
    {
        Shortcut("Programs\\Something Else.url");

        Assert.Null(ShortcutStore.FindReplacement("C:\\gone\\Parametrizacao de acesso ao IGC.url", "Parametrizacao de acesso ao IGC", [_directory]));
    }
}
