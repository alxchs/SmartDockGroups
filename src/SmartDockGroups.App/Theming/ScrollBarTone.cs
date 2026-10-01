using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace SmartDockGroups.App.Theming;

/// <summary>
/// Scrollbar colours that belong to the surface they sit on: one tone lighter than a dark
/// group, one tone darker than a light one, so the bar is visible without being the one
/// colour in the window that does not belong. The bar's thickness is not decided here — it is
/// the Windows scrollbar metric (see the <c>ScrollBar</c> style in Controls.xaml).
/// </summary>
internal static class ScrollBarTone
{
    /// <summary>Resource keys the <c>ScrollBar</c> style reads; a host overrides them in its own <c>Resources</c>.</summary>
    public const string ThumbKey = "ScrollThumbBrush";
    public const string ThumbHoverKey = "ScrollThumbHoverBrush";

    /// <summary>How far the resting thumb moves from the face colour, and how far further it goes under the mouse.</summary>
    private const double RestingShift = 0.28;
    private const double HoverShift = 0.46;

    /// <summary>
    /// Thumb colour for a surface painted <paramref name="face"/>: lighter on dark faces, darker on
    /// light ones. Each channel moves a share of the way towards white (or black), so the result is
    /// never a jump to pure white or black.
    /// </summary>
    public static Color Thumb(Color face, bool hover) => Shift(face, hover ? HoverShift : RestingShift);

    public static Color Shift(Color face, double share)
    {
        var towardsWhite = Luminance(face) < 0.5;
        byte Channel(byte value) => (byte)Math.Clamp(
            towardsWhite ? value + ((255 - value) * share) : value * (1 - share),
            0,
            255);

        return Color.FromRgb(Channel(face.R), Channel(face.G), Channel(face.B));
    }

    public static double Luminance(Color color) => ((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) / 255.0;

    /// <summary>
    /// Gives <paramref name="host"/> (a <c>ScrollViewer</c>, or any ancestor of one) scrollbar colours matched
    /// to <paramref name="surfaceHex"/>. <paramref name="seeThrough"/> is true when what shows behind the bar
    /// is a picture or the desktop, so there is no single colour to match and a soft tone of
    /// <paramref name="fallbackText"/> (the group's label colour) is used instead.
    /// </summary>
    public static void Apply(FrameworkElement host, string surfaceHex, bool seeThrough, Color fallbackText)
    {
        Color rest;
        Color hover;
        if (seeThrough)
        {
            rest = Color.FromArgb(0x88, fallbackText.R, fallbackText.G, fallbackText.B);
            hover = Color.FromArgb(0xCC, fallbackText.R, fallbackText.G, fallbackText.B);
        }
        else
        {
            var face = (Color)ColorConverter.ConvertFromString(surfaceHex)!;
            rest = Thumb(face, hover: false);
            hover = Thumb(face, hover: true);
        }

        host.Resources[ThumbKey] = Frozen(rest);
        host.Resources[ThumbHoverKey] = Frozen(hover);
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
