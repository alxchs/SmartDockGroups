namespace SmartDockGroups.Core.Models;

public enum NavigationKey { Left, Right, Up, Down, PageUp, PageDown, Home, End }

/// <summary>
/// Keyboard movement over icons as they are laid out on screen, not in the order they were
/// created or sorted: rows are found from the icons' own positions, left-to-right inside a row.
/// Nothing has an end — past the last icon comes the first, and the other way round.
/// </summary>
public static class TileNavigation
{
    /// <summary>Entries in reading order: row by row, left to right. Icons whose Y differs by less than <paramref name="rowTolerance"/> share a row.</summary>
    public static List<List<int>> Rows(IReadOnlyList<(double X, double Y)> positions, double rowTolerance)
    {
        var rows = new List<List<int>>();
        var rowTop = double.NaN;
        foreach (var index in Enumerable.Range(0, positions.Count).OrderBy(i => positions[i].Y).ThenBy(i => positions[i].X))
        {
            if (rows.Count == 0 || positions[index].Y - rowTop > rowTolerance)
            {
                rows.Add([]);
                rowTop = positions[index].Y;
            }

            rows[^1].Add(index);
        }

        foreach (var row in rows)
        {
            row.Sort((a, b) => positions[a].X.CompareTo(positions[b].X));
        }

        return rows;
    }

    public static List<int> ReadingOrder(IReadOnlyList<(double X, double Y)> positions, double rowTolerance) =>
        Rows(positions, rowTolerance).SelectMany(row => row).ToList();

    /// <summary>The index to select after <paramref name="key"/>, given the one selected now (-1 for none).</summary>
    public static int Move(IReadOnlyList<(double X, double Y)> positions, int current, NavigationKey key, int pageRows, double rowTolerance)
    {
        if (positions.Count == 0)
        {
            return -1;
        }

        var rows = Rows(positions, rowTolerance);
        var flat = rows.SelectMany(row => row).ToList();

        if (current < 0 || current >= positions.Count)
        {
            return key is NavigationKey.Left or NavigationKey.Up or NavigationKey.PageUp or NavigationKey.End
                ? flat[^1]
                : flat[0];
        }

        var position = flat.IndexOf(current);
        var rowIndex = rows.FindIndex(row => row.Contains(current));

        int Nearest(int targetRow) => rows[targetRow].OrderBy(i => Math.Abs(positions[i].X - positions[current].X)).First();

        switch (key)
        {
            case NavigationKey.Left:
                return flat[(position - 1 + flat.Count) % flat.Count];
            case NavigationKey.Right:
                return flat[(position + 1) % flat.Count];
            case NavigationKey.Home:
                return flat[0];
            case NavigationKey.End:
                return flat[^1];
            case NavigationKey.Up:
                return Nearest((rowIndex - 1 + rows.Count) % rows.Count);
            case NavigationKey.Down:
                return Nearest((rowIndex + 1) % rows.Count);
            case NavigationKey.PageUp:
                return Nearest(rowIndex == 0 ? rows.Count - 1 : Math.Max(0, rowIndex - Math.Max(1, pageRows)));
            case NavigationKey.PageDown:
                return Nearest(rowIndex == rows.Count - 1 ? 0 : Math.Min(rows.Count - 1, rowIndex + Math.Max(1, pageRows)));
            default:
                return current;
        }
    }
}
