using System.Windows.Media;
using SmartDockGroups.App.Theming;
using Xunit;
using Color = System.Windows.Media.Color;

namespace SmartDockGroups.Tests;

/// <summary>The scrollbar takes a tone of the group's own colour: a step lighter on dark, a step darker on light.</summary>
public sealed class ScrollBarToneTests
{
    [Theory]
    [InlineData(0x00, 0x00, 0x3C)]
    [InlineData(0x1E, 0x1E, 0x1E)]
    [InlineData(0x40, 0x00, 0x40)]
    [InlineData(0x00, 0x5A, 0xC8)]
    public void On_a_dark_group_the_bar_is_lighter_but_keeps_the_hue_and_is_not_white(byte r, byte g, byte b)
    {
        var face = Color.FromRgb(r, g, b);
        var rest = ScrollBarTone.Thumb(face, hover: false);
        var hover = ScrollBarTone.Thumb(face, hover: true);

        Assert.True(ScrollBarTone.Luminance(rest) > ScrollBarTone.Luminance(face));
        Assert.True(ScrollBarTone.Luminance(hover) > ScrollBarTone.Luminance(rest));
        Assert.NotEqual(Colors.White, rest);
        Assert.True(rest.B >= rest.R || face.B < face.R, "a blue-ish group stays blue-ish");
    }

    [Theory]
    [InlineData(0xF3, 0xF3, 0xF3)]
    [InlineData(0xFF, 0xFF, 0xFF)]
    [InlineData(0xE8, 0xF1, 0xFB)]
    public void On_a_light_group_the_bar_is_darker_and_not_black(byte r, byte g, byte b)
    {
        var face = Color.FromRgb(r, g, b);
        var rest = ScrollBarTone.Thumb(face, hover: false);
        var hover = ScrollBarTone.Thumb(face, hover: true);

        Assert.True(ScrollBarTone.Luminance(rest) < ScrollBarTone.Luminance(face));
        Assert.True(ScrollBarTone.Luminance(hover) < ScrollBarTone.Luminance(rest));
        Assert.NotEqual(Colors.Black, rest);
    }

    [Fact]
    public void A_group_fully_white_or_fully_black_still_gets_a_visible_bar()
    {
        Assert.NotEqual(Colors.Black, ScrollBarTone.Thumb(Colors.Black, hover: false));
        Assert.NotEqual(Colors.White, ScrollBarTone.Thumb(Colors.White, hover: false));
    }
}
