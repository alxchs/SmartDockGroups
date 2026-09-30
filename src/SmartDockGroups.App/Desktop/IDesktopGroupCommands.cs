using SmartDockGroups.Core.Models;

namespace SmartDockGroups.App.Desktop;

/// <summary>
/// Actions a group can ask for that reach beyond itself — creating a sibling, rewriting
/// the other groups, or changing what every future group inherits. A group window owns
/// only its own category, so these are handed in from whoever owns the configuration.
/// Subfolder windows get none of this: they are not desktop groups.
/// </summary>
internal interface IDesktopGroupCommands
{
    /// <summary>Asks for a name and creates a new, empty group where the mouse is.</summary>
    void CreateGroup();

    void Duplicate(MenuCategory source);

    /// <summary>
    /// Hands the chosen aspects of the source group's look to every other desktop group,
    /// or (<paramref name="asDefault"/>) makes them what new groups start with. Only the
    /// chosen aspects move: sharing the image leaves each group's colour alone, and so on.
    /// </summary>
    void ShareVisual(MenuCategory source, VisualAspects aspects, bool asDefault);

    /// <summary>Uses the Windows wallpaper as the background of one group, or of all of them.</summary>
    void ApplyWallpaper(MenuCategory? target);

    /// <summary>Creates a shortcut that brings this group forward and shows it, ready to pin to the taskbar.</summary>
    void CreateTaskbarShortcut(MenuCategory source);

    /// <summary>Opens the app's global settings — reachable from any group, not just the tray.</summary>
    void OpenSettings();
}
