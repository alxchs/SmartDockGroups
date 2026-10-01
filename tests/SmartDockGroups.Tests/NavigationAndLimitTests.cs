using SmartDockGroups.Core.Models;
using Xunit;

namespace SmartDockGroups.Tests;

/// <summary>Arrow/Page/Home/End by screen position with wrap-around, the 30-entry limit and shortcuts to groups.</summary>
public sealed class NavigationAndLimitTests
{
    // Created in a jumbled order on purpose; on screen: row 0 = [1 (x10), 3 (x100), 0 (x200)], row 1 = [4 (x10), 2 (x100)].
    private static readonly (double X, double Y)[] Layout =
    [
        (200, 10), (10, 12), (100, 100), (100, 8), (10, 105)
    ];

    private static int Move(int current, NavigationKey key, int pageRows = 1) =>
        TileNavigation.Move(Layout, current, key, pageRows, rowTolerance: 40);

    [Fact]
    public void Reading_order_follows_the_screen_not_the_creation_order()
    {
        Assert.Equal(new[] { 1, 3, 0, 4, 2 }, TileNavigation.ReadingOrder(Layout, 40));
    }

    [Fact]
    public void Right_and_left_walk_the_reading_order_and_wrap_at_both_ends()
    {
        Assert.Equal(3, Move(1, NavigationKey.Right));
        Assert.Equal(4, Move(0, NavigationKey.Right));
        Assert.Equal(1, Move(2, NavigationKey.Right));
        Assert.Equal(2, Move(1, NavigationKey.Left));
        Assert.Equal(0, Move(4, NavigationKey.Left));
    }

    [Fact]
    public void Up_and_down_keep_the_nearest_column_and_wrap_between_first_and_last_row()
    {
        Assert.Equal(2, Move(3, NavigationKey.Down));
        Assert.Equal(3, Move(2, NavigationKey.Up));
        Assert.Equal(2, Move(0, NavigationKey.Down));
        Assert.Equal(2, Move(3, NavigationKey.Up));
        Assert.Equal(3, Move(2, NavigationKey.Down));
    }

    [Fact]
    public void Home_and_End_go_to_the_first_and_last_icon_on_screen()
    {
        Assert.Equal(1, Move(2, NavigationKey.Home));
        Assert.Equal(2, Move(1, NavigationKey.End));
    }

    [Fact]
    public void Page_keys_jump_rows_and_wrap_from_the_edge_row()
    {
        Assert.Equal(2, Move(3, NavigationKey.PageDown, pageRows: 5));
        Assert.Equal(3, Move(2, NavigationKey.PageDown));
        Assert.Equal(2, Move(3, NavigationKey.PageUp));
    }

    [Fact]
    public void With_nothing_selected_forward_keys_start_at_the_first_and_backward_keys_at_the_last()
    {
        Assert.Equal(1, Move(-1, NavigationKey.Right));
        Assert.Equal(1, Move(-1, NavigationKey.Down));
        Assert.Equal(2, Move(-1, NavigationKey.Left));
        Assert.Equal(2, Move(-1, NavigationKey.End));
    }

    private static MenuCategory Group(string id, int items)
    {
        var group = new MenuCategory { Name = id, Id = id };
        for (var i = 0; i < items; i++)
        {
            group.Items.Add(new LaunchItem { Name = "i" + i, Type = LaunchItemType.Application, Target = "x" });
        }

        return group;
    }

    [Fact]
    public void A_group_takes_entries_up_to_thirty_and_turns_the_rest_away()
    {
        var group = Group("g", 28);
        var incoming = Enumerable.Range(0, 5).Select(i => (object)new LaunchItem { Name = "n" + i, Type = LaunchItemType.File, Target = "y" });

        var (accepted, overLimit, refused) = GroupLimits.Admit(group, incoming);

        Assert.Equal(2, accepted.Count);
        Assert.Equal(3, overLimit);
        Assert.Equal(0, refused);
        Assert.Equal(0, GroupLimits.Room(Group("full", 30)));
        Assert.Equal(0, GroupLimits.Room(Group("older", 41)));
    }

    [Fact]
    public void Subfolders_count_towards_the_limit()
    {
        var group = Group("g", 29);
        group.Categories.Add(new MenuCategory { Name = "sub" });

        Assert.Equal(0, GroupLimits.Room(group));
    }

    [Fact]
    public void A_group_cannot_link_to_itself_or_twice_to_the_same_group()
    {
        var home = Group("home", 0);
        var other = Group("other", 0);

        Assert.False(GroupLimits.CanHoldLinkTo(home, "home"));
        Assert.True(GroupLimits.CanHoldLinkTo(home, "other"));

        home.Items.Add(GroupLimits.CreateGroupLink(home, other));
        Assert.False(GroupLimits.CanHoldLinkTo(home, "other"));
        Assert.Equal("other", home.Items[0].Name);
        Assert.Equal(LaunchItemType.GroupLink, home.Items[0].Type);
    }

    [Fact]
    public void Moving_links_in_refuses_a_second_link_to_the_same_group_even_inside_one_batch()
    {
        var home = Group("home", 0);
        var a = new LaunchItem { Name = "a", Type = LaunchItemType.GroupLink, Target = "t" };
        var b = new LaunchItem { Name = "b", Type = LaunchItemType.GroupLink, Target = "t" };
        var self = new LaunchItem { Name = "s", Type = LaunchItemType.GroupLink, Target = "home" };

        var (accepted, _, refused) = GroupLimits.Admit(home, [a, b, self]);

        Assert.Equal(new object[] { a }, accepted);
        Assert.Equal(2, refused);
    }

    [Fact]
    public void A_new_link_takes_the_target_name_made_unique_inside_the_group()
    {
        var home = Group("home", 0);
        home.Items.Add(new LaunchItem { Name = "Work", Type = LaunchItemType.File, Target = "z" });
        var target = new MenuCategory { Name = "Work", Id = "w" };

        Assert.Equal("Work (2)", GroupLimits.CreateGroupLink(home, target).Name);
    }
}
