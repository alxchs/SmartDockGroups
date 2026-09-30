using System.IO;
using SmartDockGroups.App.Services;
using SmartDockGroups.Core.Configuration;
using SmartDockGroups.Core.Models;
using Xunit;

namespace SmartDockGroups.Tests;

/// <summary>
/// "Dock all groups": the stack's geometry, what undocking restores, that the state survives a
/// restart, and that every desktop-menu verb is one the app handles.
/// </summary>
public sealed class DockTests
{
    private static readonly DockMember[] Three =
    [
        new("a", 30, 400),
        new("b", 30, 300),
        new("c", 30, 200)
    ];

    [Fact]
    public void Collapsed_the_stack_is_one_title_bar_after_another()
    {
        var (top, slots) = DockLayout.Arrange(Three, expandedId: null, top: 100, workTop: 0, workBottom: 1000);

        Assert.Equal(100, top);
        Assert.Equal(new[] { 100.0, 130.0, 160.0 }, slots.Select(s => s.Top));
        Assert.All(slots, s => Assert.False(s.Expanded));
    }

    [Fact]
    public void Expanding_one_pushes_the_ones_below_it_down_by_its_extra_height()
    {
        var (_, slots) = DockLayout.Arrange(Three, expandedId: "b", top: 100, workTop: 0, workBottom: 1000);

        Assert.Equal((100.0, 30.0), (slots[0].Top, slots[0].Height));
        Assert.Equal((130.0, 300.0, true), (slots[1].Top, slots[1].Height, slots[1].Expanded));
        Assert.Equal(430, slots[2].Top);
    }

    [Fact]
    public void Only_the_named_group_is_ever_expanded()
    {
        var (_, slots) = DockLayout.Arrange(Three, expandedId: "c", top: 0, workTop: 0, workBottom: 1000);

        Assert.Single(slots, s => s.Expanded);
    }

    [Fact]
    public void An_expanded_group_is_cut_to_the_room_left_above_the_bottom_of_the_screen()
    {
        var (_, slots) = DockLayout.Arrange(Three, expandedId: "a", top: 500, workTop: 0, workBottom: 800);

        // 800 - 500 - two other title bars = 240, less than the 400 it would like.
        Assert.Equal(240, slots[0].Height);
        Assert.Equal(800, slots[2].Top + slots[2].Height);
    }

    [Fact]
    public void Near_the_bottom_the_whole_stack_moves_up_rather_than_off_the_screen()
    {
        var (top, slots) = DockLayout.Arrange(Three, expandedId: "a", top: 760, workTop: 48, workBottom: 800);

        Assert.True(top < 760);
        Assert.True(slots[0].Height >= DockLayout.MinExpandedHeight);
        Assert.Equal(800, slots[2].Top + slots[2].Height);
    }

    [Fact]
    public void The_stack_never_starts_above_the_work_area_even_with_a_taskbar_on_top()
    {
        var (top, _) = DockLayout.Arrange(Three, expandedId: null, top: 10, workTop: 48, workBottom: 1000);

        Assert.Equal(48, top);
    }

    [Fact]
    public void Undocking_restores_the_placement_taken_before_docking()
    {
        var group = new MenuCategory
        {
            Name = "G", Id = "g1", DesktopX = 321, DesktopY = 654, DesktopWidth = 480, DesktopHeight = 360,
            IsCollapsed = false, DisplayMode = DesktopGroupDisplayMode.AppFolder, PanelX = 10, PanelY = 20
        };
        var saved = GroupPlacement.From(group);

        group.DesktopX = 0;
        group.DesktopY = 0;
        group.DesktopWidth = 200;
        group.DesktopHeight = 30;
        group.IsCollapsed = true;
        group.DisplayMode = DesktopGroupDisplayMode.Panel;
        saved.ApplyTo(group);

        Assert.Equal((321.0, 654.0, 480.0, 360.0), (group.DesktopX, group.DesktopY, group.DesktopWidth, group.DesktopHeight));
        Assert.False(group.IsCollapsed);
        Assert.Equal(DesktopGroupDisplayMode.AppFolder, group.DisplayMode);
        Assert.Equal((10.0, 20.0), (group.PanelX, group.PanelY));
    }

    [Fact]
    public void The_dock_survives_a_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), "SmartDockGroupsTests", Guid.NewGuid().ToString("N"), "config.json");
        try
        {
            var configuration = new LauncherConfiguration();
            configuration.Dock = new DockState
            {
                IsDocked = true, Left = 40, Top = 80, Width = 300, ExpandedId = "b", Order = ["a", "b"],
                Saved = [new GroupPlacement { Id = "a", X = 1, Y = 2, Width = 3, Height = 4, IsCollapsed = true }]
            };

            new ConfigurationStore(path).Save(configuration);
            Assert.True(new ConfigurationStore(path).TryLoad(out var reloaded));

            Assert.True(reloaded.Dock.IsDocked);
            Assert.Equal(new[] { "a", "b" }, reloaded.Dock.Order);
            Assert.Equal("b", reloaded.Dock.ExpandedId);
            Assert.Equal(4, reloaded.Dock.Saved.Single().Height);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Every_desktop_menu_verb_is_unique_and_labelled()
    {
        var verbs = DesktopContextMenuRegistration.Verbs;

        Assert.Equal(verbs.Length, verbs.Select(v => v.Action).Distinct().Count());
        Assert.Equal(verbs.Length, verbs.Select(v => v.Key).Distinct().Count());
        Assert.Contains(verbs, v => v.Action == DesktopContextMenuRegistration.BringAllToFrontAction);
        Assert.Contains(verbs, v => v.Action == DesktopContextMenuRegistration.SendAllToBackAction);
        Assert.Contains(verbs, v => v.Action == DesktopContextMenuRegistration.DockAllAction);
        Assert.Contains(verbs, v => v.Action == DesktopContextMenuRegistration.UndockAllAction);
        foreach (var language in new[] { "pt", "en", "es", "de", "it", "pl", "ru", "ja" })
        {
            Assert.All(verbs, v => Assert.NotEqual(v.LabelKey, SmartDockGroups.App.Localization.LocalizationService.GetForLanguage(language, v.LabelKey)));
        }
    }
}
