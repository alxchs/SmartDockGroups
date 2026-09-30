namespace SmartDockGroups.Core.Models;

/// <summary>
/// "Dock all groups": every open group stacked in one column, collapsed, at most one expanded
/// at a time (the others pushed below it). Everything needed to put each group back exactly
/// where it was is kept in <see cref="Saved"/>, taken at the moment of docking.
/// </summary>
public sealed class DockState
{
    public bool IsDocked { get; set; }

    /// <summary>Top-left of the stack and its width, in the same units as a group's position.</summary>
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }

    /// <summary>Group ids, top to bottom.</summary>
    public List<string> Order { get; set; } = [];

    /// <summary>The one group shown expanded, or null when all are collapsed.</summary>
    public string? ExpandedId { get; set; }

    /// <summary>Each docked group as it was just before it joined the stack.</summary>
    public List<GroupPlacement> Saved { get; set; } = [];

    public DockState Clone()
    {
        return new DockState
        {
            IsDocked = IsDocked,
            Left = Left,
            Top = Top,
            Width = Width,
            Order = [.. Order],
            ExpandedId = ExpandedId,
            Saved = [.. Saved.Select(placement => placement.Clone())]
        };
    }
}

/// <summary>Where and how a group was shown — what undocking restores.</summary>
public sealed class GroupPlacement
{
    public required string Id { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool IsCollapsed { get; set; }
    public DesktopGroupDisplayMode DisplayMode { get; set; }
    public double? PanelX { get; set; }
    public double? PanelY { get; set; }

    public static GroupPlacement From(MenuCategory group) => new()
    {
        Id = group.Id ?? throw new InvalidOperationException("A docked group needs an Id."),
        X = group.DesktopX,
        Y = group.DesktopY,
        Width = group.DesktopWidth,
        Height = group.DesktopHeight,
        IsCollapsed = group.IsCollapsed,
        DisplayMode = group.DisplayMode,
        PanelX = group.PanelX,
        PanelY = group.PanelY
    };

    public void ApplyTo(MenuCategory group)
    {
        group.DesktopX = X;
        group.DesktopY = Y;
        group.DesktopWidth = Width;
        group.DesktopHeight = Height;
        group.IsCollapsed = IsCollapsed;
        group.DisplayMode = DisplayMode;
        group.PanelX = PanelX;
        group.PanelY = PanelY;
    }

    public GroupPlacement Clone() => (GroupPlacement)MemberwiseClone();
}

/// <summary>One group in the stack, as the layout needs it.</summary>
/// <param name="HeaderHeight">Height of the group collapsed to its title bar.</param>
/// <param name="PreferredHeight">Height it had before docking — what it opens to when there is room.</param>
public readonly record struct DockMember(string Id, double HeaderHeight, double PreferredHeight);

/// <summary>Where one docked group goes.</summary>
public readonly record struct DockSlot(string Id, double Top, double Height, bool Expanded);

/// <summary>The stack's geometry, as a pure function so it can be tested without windows.</summary>
public static class DockLayout
{
    /// <summary>Smallest height an expanded group is given, even when that means moving the stack up.</summary>
    public const double MinExpandedHeight = 120;

    /// <summary>
    /// Lays the members out top to bottom from <paramref name="top"/>: every group at its header
    /// height, except <paramref name="expandedId"/>, which gets its preferred height or whatever
    /// fits above <paramref name="workBottom"/>. When even the collapsed stack (plus a minimum
    /// for the expanded one) does not fit, the whole stack moves up — never above
    /// <paramref name="workTop"/>. Returns the top actually used and one slot per member.
    /// </summary>
    public static (double Top, IReadOnlyList<DockSlot> Slots) Arrange(
        IReadOnlyList<DockMember> members,
        string? expandedId,
        double top,
        double workTop,
        double workBottom)
    {
        var expanded = members.FirstOrDefault(m => m.Id == expandedId);
        var hasExpanded = expanded.Id is not null;
        var headers = members.Sum(m => m.HeaderHeight);
        var others = hasExpanded ? headers - expanded.HeaderHeight : headers;
        var minimumNeeded = hasExpanded ? others + Math.Max(expanded.HeaderHeight, MinExpandedHeight) : headers;

        if (top + minimumNeeded > workBottom)
        {
            top = workBottom - minimumNeeded;
        }

        top = Math.Max(top, workTop);

        var expandedHeight = 0.0;
        if (hasExpanded)
        {
            var room = workBottom - top - others;
            expandedHeight = Math.Max(expanded.HeaderHeight, Math.Min(Math.Max(expanded.PreferredHeight, MinExpandedHeight), room));
        }

        var slots = new List<DockSlot>(members.Count);
        var y = top;
        foreach (var member in members)
        {
            var isExpanded = hasExpanded && member.Id == expandedId;
            var height = isExpanded ? expandedHeight : member.HeaderHeight;
            slots.Add(new DockSlot(member.Id, y, height, isExpanded));
            y += height;
        }

        return (top, slots);
    }
}
