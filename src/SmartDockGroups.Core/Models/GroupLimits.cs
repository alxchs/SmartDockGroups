namespace SmartDockGroups.Core.Models;

/// <summary>
/// How many entries a group holds, and which of them it will take. A group is a place to find
/// things at a glance; past <see cref="MaxEntries"/> it stops being one, and a second group is
/// the better answer.
/// </summary>
public static class GroupLimits
{
    public const int MaxEntries = 30;

    /// <summary>Shortcuts plus subfolders.</summary>
    public static int Count(MenuCategory group) => group.Items.Count + group.Categories.Count;

    /// <summary>How many more entries fit. Never negative: a group over the limit from an older version just takes none.</summary>
    public static int Room(MenuCategory group) => Math.Max(0, MaxEntries - Count(group));

    /// <summary>
    /// A shortcut to a group is refused when it points at the group it sits in, or when that
    /// group already holds one for the same target.
    /// </summary>
    public static bool CanHoldLinkTo(MenuCategory container, string targetGroupId)
    {
        return !string.Equals(container.Id, targetGroupId, StringComparison.Ordinal)
            && !container.Items.Any(i => i.Type == LaunchItemType.GroupLink
                && string.Equals(i.Target, targetGroupId, StringComparison.Ordinal));
    }

    public static LaunchItem CreateGroupLink(MenuCategory container, MenuCategory target)
    {
        return new LaunchItem
        {
            Name = GroupNames.MakeUnique(container, target.Name),
            Type = LaunchItemType.GroupLink,
            Target = target.Id ?? throw new InvalidOperationException("The target group has no Id."),
            IsDesktopPinned = true
        };
    }

    /// <summary>
    /// Splits entries arriving in <paramref name="container"/> into those it takes and those it
    /// turns away, keeping their order: first what a group-link rule refuses, then whatever
    /// no longer fits under the limit.
    /// </summary>
    public static (List<object> Accepted, int OverLimit, int RefusedLinks) Admit(MenuCategory container, IEnumerable<object> entries)
    {
        var accepted = new List<object>();
        var room = Room(container);
        var overLimit = 0;
        var refused = 0;
        var linkTargets = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (entry is LaunchItem { Type: LaunchItemType.GroupLink } link
                && (!CanHoldLinkTo(container, link.Target) || !linkTargets.Add(link.Target)))
            {
                refused++;
                continue;
            }

            if (accepted.Count >= room)
            {
                overLimit++;
                continue;
            }

            accepted.Add(entry);
        }

        return (accepted, overLimit, refused);
    }
}
