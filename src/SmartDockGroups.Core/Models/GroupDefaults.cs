namespace SmartDockGroups.Core.Models;

/// <summary>
/// What a new group starts with, aspect by aspect, beyond the default theme
/// (<see cref="LauncherConfiguration.Theme"/>, which carries the colour). A null field
/// means "no preference", so the group keeps the built-in value.
/// </summary>
public sealed class GroupDefaults
{
    public string? BackgroundImagePath { get; set; }
    public double? AreaOpacity { get; set; }
    public double? TitleOpacity { get; set; }
    public int? IconHGap { get; set; }
    public int? IconVGap { get; set; }
    public double? IconScale { get; set; }

    /// <summary>Remembers the chosen aspects of <paramref name="source"/> as the default for new groups.</summary>
    public void TakeFrom(MenuCategory source, VisualAspects aspects)
    {
        if (aspects.HasFlag(VisualAspects.BackgroundImage))
        {
            BackgroundImagePath = source.DesktopBackgroundImagePath;
        }

        if (aspects.HasFlag(VisualAspects.Opacity))
        {
            AreaOpacity = source.AreaOpacity;
            TitleOpacity = source.TitleOpacity;
        }

        if (aspects.HasFlag(VisualAspects.IconSpacing))
        {
            IconHGap = source.IconHGap;
            IconVGap = source.IconVGap;
        }

        if (aspects.HasFlag(VisualAspects.IconSize))
        {
            IconScale = source.DesktopIconScale;
        }
    }

    public void ApplyTo(MenuCategory group)
    {
        if (BackgroundImagePath is not null)
        {
            group.DesktopBackgroundImagePath = BackgroundImagePath;
        }

        group.AreaOpacity = AreaOpacity ?? group.AreaOpacity;
        group.TitleOpacity = TitleOpacity ?? group.TitleOpacity;
        group.IconHGap = IconHGap ?? group.IconHGap;
        group.IconVGap = IconVGap ?? group.IconVGap;
        group.DesktopIconScale = IconScale ?? group.DesktopIconScale;
    }

    public GroupDefaults Clone()
    {
        return new GroupDefaults
        {
            BackgroundImagePath = BackgroundImagePath,
            AreaOpacity = AreaOpacity,
            TitleOpacity = TitleOpacity,
            IconHGap = IconHGap,
            IconVGap = IconVGap,
            IconScale = IconScale
        };
    }
}
