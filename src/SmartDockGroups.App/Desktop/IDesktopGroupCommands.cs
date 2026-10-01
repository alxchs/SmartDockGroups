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

    /// <summary>Every desktop group, for "shortcut to a group".</summary>
    IReadOnlyList<MenuCategory> DesktopGroups { get; }

    /// <summary>What a shortcut to a group does: open it if closed, show it, focus its first icon.</summary>
    void FocusGroup(string groupId);

    /// <summary>True while the groups are docked in one stack.</summary>
    bool IsDocked { get; }

    /// <summary>Docks every open group into a stack starting where <paramref name="anchor"/> is, or undocks them all.</summary>
    void ToggleDock(MenuCategory anchor);

    /// <summary>Opens this docked group (closing whichever was open), or closes it when it is the open one.</summary>
    void DockToggleExpanded(MenuCategory member);

    /// <summary>The style (panel or app folder) a docked group will have again when the groups are undocked.</summary>
    DesktopGroupDisplayMode DockedStyle(MenuCategory member);

    /// <summary>Flips that style while docked: the group stays a title bar in the stack, and comes back in the other style on undock.</summary>
    void DockToggleStyle(MenuCategory member);

    /// <summary>The user is dragging a docked group right now: the rest of the stack moves with it, live.</summary>
    void DockFollow(MenuCategory member, double left, double top);

    /// <summary>The user dragged a docked group to here: the whole stack follows.</summary>
    void DockMovedTo(MenuCategory member, double left, double top);

    /// <summary>Carries the whole stack into a monitor's work area (the "all groups to monitor" commands).</summary>
    void MoveDockInto(System.Windows.Rect workArea);
}
