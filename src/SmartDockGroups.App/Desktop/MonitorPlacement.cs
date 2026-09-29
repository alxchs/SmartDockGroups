using System.Windows;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace SmartDockGroups.App.Desktop;

/// <summary>
/// Where a group should sit when the set of monitors is not the one it was placed on.
/// Pure geometry over work areas in device-independent pixels, kept apart from the
/// windows so the rules can be checked without a second monitor plugged in.
/// </summary>
internal static class MonitorPlacement
{
    /// <summary>How much of the title strip must be on screen for a group to be grabbable.</summary>
    private const double MinVisibleWidth = 48;
    private const double MinVisibleHeight = 16;
    private const double TitleStripHeight = 32;

    /// <summary>
    /// True when the group can still be reached with the mouse: its title strip overlaps
    /// some work area by enough to grab. Merely touching a corner does not count.
    /// </summary>
    public static bool IsReachable(Rect group, IReadOnlyList<Rect> workAreas)
    {
        var strip = new Rect(group.X, group.Y, group.Width, Math.Min(TitleStripHeight, group.Height));
        foreach (var area in workAreas)
        {
            var overlap = Rect.Intersect(strip, area);
            if (!overlap.IsEmpty
                && overlap.Width >= Math.Min(MinVisibleWidth, group.Width)
                && overlap.Height >= Math.Min(MinVisibleHeight, strip.Height))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The work area a group belongs to: the one it overlaps most, or failing that the one
    /// whose centre is nearest to it. Returns -1 only when there are no work areas.
    /// </summary>
    public static int IndexOfOwner(Rect group, IReadOnlyList<Rect> workAreas)
    {
        var best = -1;
        var bestOverlap = 0.0;
        for (var i = 0; i < workAreas.Count; i++)
        {
            var overlap = Rect.Intersect(group, workAreas[i]);
            var area = overlap.IsEmpty ? 0 : overlap.Width * overlap.Height;
            if (area > bestOverlap)
            {
                bestOverlap = area;
                best = i;
            }
        }

        if (best >= 0)
        {
            return best;
        }

        var centre = new Point(group.X + (group.Width / 2), group.Y + (group.Height / 2));
        var nearest = double.MaxValue;
        for (var i = 0; i < workAreas.Count; i++)
        {
            var distance = DistanceSquared(centre, workAreas[i]);
            if (distance < nearest)
            {
                nearest = distance;
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// The monitor next to <paramref name="fromIndex"/> in a direction (-1 left, +1 right),
    /// ordered by horizontal centre and wrapping around at the ends, the way Windows'
    /// own Win+Shift+arrow does. Null when there is only one monitor.
    /// </summary>
    public static int? AdjacentIndex(int fromIndex, IReadOnlyList<Rect> workAreas, int direction)
    {
        if (workAreas.Count < 2 || fromIndex < 0 || fromIndex >= workAreas.Count)
        {
            return null;
        }

        var ordered = Enumerable.Range(0, workAreas.Count)
            .OrderBy(i => workAreas[i].X + (workAreas[i].Width / 2))
            .ThenBy(i => workAreas[i].Y)
            .ToList();

        var position = ordered.IndexOf(fromIndex);
        var next = ((position + Math.Sign(direction)) % ordered.Count + ordered.Count) % ordered.Count;
        return ordered[next];
    }

    /// <summary>
    /// Carries a group from one work area to another keeping its relative place — a group
    /// in the top-right corner of one monitor lands in the top-right corner of the other —
    /// and then keeps it fully inside the destination.
    /// </summary>
    public static Point MapBetween(Rect group, Rect from, Rect to)
    {
        var relativeX = Relative(group.X, from.X, from.Width, group.Width);
        var relativeY = Relative(group.Y, from.Y, from.Height, group.Height);

        var target = new Point(
            to.X + (relativeX * Math.Max(0, to.Width - group.Width)),
            to.Y + (relativeY * Math.Max(0, to.Height - group.Height)));

        return ClampInto(target, group.Size, to);
    }

    /// <summary>Keeps a rectangle of <paramref name="size"/> inside <paramref name="area"/>.</summary>
    public static Point ClampInto(Point position, Size size, Rect area)
    {
        var maxX = Math.Max(area.X, area.Right - size.Width);
        var maxY = Math.Max(area.Y, area.Bottom - size.Height);
        return new Point(Math.Clamp(position.X, area.X, maxX), Math.Clamp(position.Y, area.Y, maxY));
    }

    /// <summary>
    /// Nudges a position diagonally until it no longer sits on top of one already taken,
    /// so groups rescued from the same corner do not end up stacked exactly on each other.
    /// </summary>
    public static Point AvoidStacking(Point position, Size size, Rect area, ICollection<Point> taken, double step = 28)
    {
        var candidate = position;
        for (var attempt = 0; attempt < 12 && taken.Any(p => Math.Abs(p.X - candidate.X) < 4 && Math.Abs(p.Y - candidate.Y) < 4); attempt++)
        {
            candidate = ClampInto(new Point(candidate.X + step, candidate.Y + step), size, area);
        }

        taken.Add(candidate);
        return candidate;
    }

    private static double Relative(double position, double start, double span, double size)
    {
        var travel = span - size;
        return travel <= 0 ? 0 : Math.Clamp((position - start) / travel, 0, 1);
    }

    private static double DistanceSquared(Point point, Rect area)
    {
        var dx = Math.Max(Math.Max(area.X - point.X, 0), point.X - area.Right);
        var dy = Math.Max(Math.Max(area.Y - point.Y, 0), point.Y - area.Bottom);
        return (dx * dx) + (dy * dy);
    }

    /// <summary>
    /// Arranges a collection of group rectangles centered on <paramref name="destArea"/>
    /// without changing their sizes, keeping them compact and close to the monitor's center.
    /// </summary>
    public static IReadOnlyList<Point> ArrangeCentered(IReadOnlyList<Size> sizes, Rect destArea, double gap = 20)
    {
        var count = sizes.Count;
        if (count == 0)
        {
            return Array.Empty<Point>();
        }

        if (count == 1)
        {
            var single = sizes[0];
            var x = destArea.X + Math.Max(0, (destArea.Width - single.Width) / 2);
            var y = destArea.Y + Math.Max(0, (destArea.Height - single.Height) / 2);
            return new[] { new Point(x, y) };
        }

        var screenAspect = destArea.Width / Math.Max(1, destArea.Height);

        var bestCols = 1;
        var bestScore = double.MaxValue;
        var bestRows = new List<List<int>>();
        var bestTotalHeight = 0.0;
        var bestRowWidths = new List<double>();
        var bestRowHeights = new List<double>();

        for (var cols = 1; cols <= count; cols++)
        {
            var rows = new List<List<int>>();
            var rowWidths = new List<double>();
            var rowHeights = new List<double>();

            var currentIdx = 0;
            while (currentIdx < count)
            {
                var row = new List<int>();
                var rowW = 0.0;
                var rowH = 0.0;
                for (var c = 0; c < cols && currentIdx < count; c++, currentIdx++)
                {
                    row.Add(currentIdx);
                    rowW += sizes[currentIdx].Width;
                    rowH = Math.Max(rowH, sizes[currentIdx].Height);
                }
                rowW += (row.Count - 1) * gap;
                rows.Add(row);
                rowWidths.Add(rowW);
                rowHeights.Add(rowH);
            }

            var totalW = rowWidths.Max();
            var totalH = rowHeights.Sum() + ((rows.Count - 1) * gap);

            var overflows = totalW > destArea.Width || totalH > destArea.Height;
            var layoutAspect = totalW / Math.Max(1, totalH);
            var aspectDiff = Math.Abs(Math.Log(layoutAspect / screenAspect));

            var score = (overflows ? 1000.0 : 0.0)
                        + (aspectDiff * 10.0)
                        + Math.Abs(cols - Math.Sqrt(count * screenAspect));

            if (score < bestScore)
            {
                bestScore = score;
                bestCols = cols;
                bestRows = rows;
                bestTotalHeight = totalH;
                bestRowWidths = rowWidths;
                bestRowHeights = rowHeights;
            }
        }

        var result = new Point[count];
        var startY = destArea.Y + Math.Max(0, (destArea.Height - bestTotalHeight) / 2);
        var curY = startY;

        for (var r = 0; r < bestRows.Count; r++)
        {
            var row = bestRows[r];
            var rWidth = bestRowWidths[r];
            var rHeight = bestRowHeights[r];

            var rowStartX = destArea.X + Math.Max(0, (destArea.Width - rWidth) / 2);
            var curX = rowStartX;

            foreach (var idx in row)
            {
                var itemSize = sizes[idx];
                var itemY = curY + Math.Max(0, (rHeight - itemSize.Height) / 2);
                result[idx] = new Point(curX, itemY);
                curX += itemSize.Width + gap;
            }

            curY += rHeight + gap;
        }

        return result;
    }
}
