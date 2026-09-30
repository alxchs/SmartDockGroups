using System.Text.Json.Serialization;

namespace SmartDockGroups.Core.Models;

public sealed class MenuCategory : IMenuContainer
{
    public required string Name { get; set; }

    /// <summary>
    /// Stable identity, so something outside the config (a taskbar shortcut) can point at this
    /// group and survive a rename or a duplicate name. Null in configs written before this
    /// existed; <c>DesktopOrganizerService.EnsureIds</c> fills it in and the config is saved.
    /// </summary>
    public string? Id { get; set; }
    public List<MenuCategory> Categories { get; init; } = [];
    public List<LaunchItem> Items { get; init; } = [];
    public MenuTheme? ThemeOverride { get; set; }
    public bool IsDesktopGroup { get; set; }
    public double DesktopX { get; set; } = 40;
    public double DesktopY { get; set; } = 40;
    public double DesktopWidth { get; set; } = 220;
    public double DesktopHeight { get; set; } = 180;
    public double DesktopIconScale { get; set; } = 1.0;
    public string? DesktopBackgroundImagePath { get; set; }
    public double? IconX { get; set; }
    public double? IconY { get; set; }
    public bool IsCollapsed { get; set; }
    public bool IsClosed { get; set; }
    /// <summary>0 is fully see-through, 1 fully painted. Replaces the old on/off flag.</summary>
    public double AreaOpacity { get; set; } = 1.0;
    public double TitleOpacity { get; set; } = 1.0;

    /// <summary>
    /// Reads the on/off transparency written before the sliders existed. The getter is
    /// null so the retired key is never written back into the file.
    /// </summary>
    [JsonPropertyName("AreaTransparent")]
    public bool? LegacyAreaTransparent
    {
        get => null;
        set { if (value == true) { AreaOpacity = 0; } }
    }

    [JsonPropertyName("TitleTransparent")]
    public bool? LegacyTitleTransparent
    {
        get => null;
        set { if (value == true) { TitleOpacity = 0; } }
    }
    public DesktopGroupDisplayMode DisplayMode { get; set; } = DesktopGroupDisplayMode.Panel;

    /// <summary>
    /// Where the panel sat before switching to the folder look. The folder tile is a
    /// different size, so without remembering this the panel would come back resized
    /// and moved to wherever the tile happened to be.
    /// </summary>
    public double? PanelX { get; set; }
    public double? PanelY { get; set; }

    /// <summary>Extra horizontal gap (px, pre-zoom) added between icon columns beyond the base tile size. Range 10–30, default 10.</summary>
    public int IconHGap { get; set; } = 10;

    /// <summary>Extra vertical gap (px, pre-zoom) added between icon rows beyond the base tile size. Range 10–30, default 10.</summary>
    public int IconVGap { get; set; } = 10;

    /// <summary>How this group's icons are kept laid out. See <see cref="IconArrangement"/>.</summary>
    public IconArrangement IconArrangement { get; set; } = IconArrangement.None;

    /// <summary>
    /// Takes on another group's appearance — theme, opacities, backdrop and icon spacing —
    /// while leaving its own contents, position and size alone.
    /// </summary>
    public void CopyVisualFrom(MenuCategory source) => CopyVisualFrom(source, VisualAspects.All, new MenuTheme());

    /// <summary>
    /// Takes on only the chosen parts of another group's look, leaving the rest as they
    /// are. <paramref name="defaultTheme"/> is the theme either group shows when it has no
    /// override of its own — needed to copy just the colour onto a group that has none yet.
    /// </summary>
    public void CopyVisualFrom(MenuCategory source, VisualAspects aspects, MenuTheme defaultTheme)
    {
        if (aspects == VisualAspects.All)
        {
            ThemeOverride = source.ThemeOverride?.Clone();
        }
        else if (aspects.HasFlag(VisualAspects.BackgroundColor))
        {
            var sourceTheme = source.ThemeOverride ?? defaultTheme;
            ThemeOverride ??= defaultTheme.Clone();
            ThemeOverride.BackgroundColor = sourceTheme.BackgroundColor;
            ThemeOverride.TextColor = sourceTheme.TextColor;
        }

        if (aspects.HasFlag(VisualAspects.BackgroundImage))
        {
            DesktopBackgroundImagePath = source.DesktopBackgroundImagePath;
        }

        if (aspects.HasFlag(VisualAspects.Opacity))
        {
            AreaOpacity = source.AreaOpacity;
            TitleOpacity = source.TitleOpacity;
        }

        if (aspects.HasFlag(VisualAspects.IconSize))
        {
            DesktopIconScale = source.DesktopIconScale;
        }

        if (aspects.HasFlag(VisualAspects.IconSpacing))
        {
            IconHGap = source.IconHGap;
            IconVGap = source.IconVGap;
        }
    }

    public MenuCategory Clone()
    {
        return new MenuCategory
        {
            Name = Name,
            Id = Id,
            Items = [.. Items.Select(item => item.Clone())],
            Categories = [.. Categories.Select(category => category.Clone())],
            ThemeOverride = ThemeOverride?.Clone(),
            IsDesktopGroup = IsDesktopGroup,
            DesktopX = DesktopX,
            DesktopY = DesktopY,
            DesktopWidth = DesktopWidth,
            DesktopHeight = DesktopHeight,
            DesktopIconScale = DesktopIconScale,
            DesktopBackgroundImagePath = DesktopBackgroundImagePath,
            IconX = IconX,
            IconY = IconY,
            IsCollapsed = IsCollapsed,
            IsClosed = IsClosed,
            AreaOpacity = AreaOpacity,
            TitleOpacity = TitleOpacity,
            DisplayMode = DisplayMode,
            PanelX = PanelX,
            PanelY = PanelY,
            IconHGap = IconHGap,
            IconVGap = IconVGap,
            IconArrangement = IconArrangement
        };
    }
}
