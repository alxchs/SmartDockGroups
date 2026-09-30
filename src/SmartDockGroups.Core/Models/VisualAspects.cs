namespace SmartDockGroups.Core.Models;

/// <summary>
/// The parts of a group's look that can be handed to other groups one at a time — a
/// background image without forcing the colour, the colour without forcing the image,
/// the icon spacing on its own, and so on.
/// </summary>
[Flags]
public enum VisualAspects
{
    None = 0,

    /// <summary>Background colour, and the text colour derived from it.</summary>
    BackgroundColor = 1,

    BackgroundImage = 2,

    /// <summary>Both sliders: the area and the title bar.</summary>
    Opacity = 4,

    IconSpacing = 8,

    IconSize = 16,

    /// <summary>The whole theme (fonts, highlight, corners, shadow) plus every aspect above.</summary>
    All = BackgroundColor | BackgroundImage | Opacity | IconSpacing | IconSize | 32
}
