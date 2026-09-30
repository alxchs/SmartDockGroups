namespace SmartDockGroups.Core.Models;

/// <summary>
/// A group never holds two entries with the same name — typed, pasted, dropped, moved in
/// from another group or imported. The comparison ignores case and surrounding spaces,
/// the same way Explorer treats two file names in one folder.
/// </summary>
public static class GroupNames
{
    private static bool SameName(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.CurrentCultureIgnoreCase);

    /// <summary>True when another entry of <paramref name="group"/> (not <paramref name="except"/>) already uses <paramref name="name"/>.</summary>
    public static bool IsTaken(MenuCategory group, string name, object? except = null)
    {
        return group.Items.Any(item => !ReferenceEquals(item, except) && SameName(item.Name, name))
            || group.Categories.Any(folder => !ReferenceEquals(folder, except) && SameName(folder.Name, name));
    }

    /// <summary>
    /// <paramref name="desired"/> itself when it is free, otherwise the first free
    /// "<c>Name (2)</c>", "<c>Name (3)</c>"… — the suffix Explorer gives a second copy.
    /// </summary>
    public static string MakeUnique(MenuCategory group, string desired, object? except = null)
    {
        var baseName = desired.Trim();
        if (!IsTaken(group, baseName, except))
        {
            return baseName;
        }

        for (var counter = 2; ; counter++)
        {
            var candidate = $"{baseName} ({counter})";
            if (!IsTaken(group, candidate, except))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// The item already in <paramref name="group"/> that is the same shortcut as
    /// <paramref name="item"/> — same name and same target — so adding it again would
    /// only produce a copy. Null when there is none.
    /// </summary>
    public static LaunchItem? FindSameShortcut(MenuCategory group, LaunchItem item)
    {
        return group.Items.FirstOrDefault(existing =>
            !ReferenceEquals(existing, item)
            && SameName(existing.Name, item.Name)
            && string.Equals(existing.Target, item.Target, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Renames every later entry that repeats an earlier one's name, in this group and in
    /// every group below it. For configurations written before the rule existed, or
    /// imported from a file. True when anything was renamed.
    /// </summary>
    public static bool EnsureUnique(IMenuContainer container)
    {
        var changed = false;
        foreach (var group in container.Categories)
        {
            changed |= EnsureUniqueWithin(group);
            changed |= EnsureUnique(group);
        }

        return changed;
    }

    /// <summary>The first holder of a name keeps it; only the repeats further down are renamed.</summary>
    private static bool EnsureUniqueWithin(MenuCategory group)
    {
        var changed = false;
        var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

        string Claim(string name)
        {
            var baseName = name.Trim();
            var candidate = baseName;
            for (var counter = 2; seen.Contains(candidate) || (candidate != baseName && IsTaken(group, candidate)); counter++)
            {
                candidate = $"{baseName} ({counter})";
            }

            seen.Add(candidate);
            return candidate;
        }

        foreach (var folder in group.Categories)
        {
            var unique = Claim(folder.Name);
            changed |= !string.Equals(unique, folder.Name, StringComparison.Ordinal);
            folder.Name = unique;
        }

        foreach (var item in group.Items)
        {
            var unique = Claim(item.Name);
            changed |= !string.Equals(unique, item.Name, StringComparison.Ordinal);
            item.Name = unique;
        }

        return changed;
    }
}
