using SmartDockGroups.App.Services;
using System.Windows;
using System.Windows.Threading;
using SmartDockGroups.Core.Models;
using Microsoft.Win32;

namespace SmartDockGroups.App.Desktop;

internal sealed class DesktopOrganizerService(IconCacheService iconCache)
{
    private static readonly TimeSpan DisplaySettleDelay = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan PlacementHold = TimeSpan.FromSeconds(3);

    private readonly Dictionary<MenuCategory, DesktopGroupWindow> _windows = new();
    private LauncherConfiguration? _configuration;
    private Action<MenuCategory>? _save;
    private IReadOnlyList<Rect>? _lastWorkAreas;
    private DispatcherTimer? _displaySettleTimer;

    public void StartWatchingDisplays()
    {
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public void StopWatchingDisplays()
    {
        // SystemEvents is static: a handler left attached outlives this service.
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _displaySettleTimer?.Stop();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // Raised on a system thread; the windows belong to the UI thread.
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            foreach (var window in _windows.Values)
            {
                window.HoldPlacement(PlacementHold);
            }

            // Several notifications arrive for one plug or unplug, and Windows moves its
            // own windows in the middle of them; act once, after it has finished.
            if (_displaySettleTimer is null)
            {
                _displaySettleTimer = new DispatcherTimer { Interval = DisplaySettleDelay };
                _displaySettleTimer.Tick += (_, _) =>
                {
                    _displaySettleTimer.Stop();
                    EnsureGroupsReachable();
                };
            }

            _displaySettleTimer.Stop();
            _displaySettleTimer.Start();
        });
    }

    /// <summary>
    /// Makes every group reachable on the monitors connected now. A group whose home is on
    /// screen goes home; one stranded on a monitor that went away is carried to the
    /// nearest remaining monitor, keeping its relative place when the old layout is known.
    /// Rescued positions are never saved, so a group returns home when its monitor does.
    /// </summary>
    public void EnsureGroupsReachable()
    {
        if (IsDocked)
        {
            // The stack keeps itself together; it only needs to land on a monitor that exists.
            RelayoutDock(animate: false);
            return;
        }

        var reference = _windows.Values.FirstOrDefault();
        if (reference is null)
        {
            return;
        }

        var areas = DisplayInventory.WorkAreas(reference);
        if (areas.Count == 0)
        {
            return;
        }

        var taken = new List<System.Windows.Point>();
        foreach (var window in _windows.Values)
        {
            var home = window.HomeRect;

            if (MonitorPlacement.IsReachable(home, areas))
            {
                if (Math.Abs(window.Left - home.X) > 0.5 || Math.Abs(window.Top - home.Y) > 0.5)
                {
                    window.PlaceWithoutSaving(home.X, home.Y);
                }

                continue;
            }

            var destination = areas[MonitorPlacement.IndexOfOwner(home, areas)];

            var position = _lastWorkAreas is { Count: > 0 } previous && MonitorPlacement.IsReachable(home, previous)
                ? MonitorPlacement.MapBetween(home, previous[MonitorPlacement.IndexOfOwner(home, previous)], destination)
                : MonitorPlacement.ClampInto(home.Location, home.Size, destination);

            position = MonitorPlacement.AvoidStacking(position, home.Size, destination, taken);
            window.PlaceWithoutSaving(position.X, position.Y);
        }

        // Only a layout that shows every home is worth remembering as the one to map from.
        if (_windows.Values.All(window => MonitorPlacement.IsReachable(window.HomeRect, areas)))
        {
            _lastWorkAreas = areas;
        }
    }

    /// <summary>Re-reads every open group's appearance from the configuration.</summary>
    /// <summary>Brings back every group Windows minimized and then failed to restore.</summary>
    public void RestoreMinimizedGroups()
    {
        foreach (var window in _windows.Values)
        {
            window.RestoreIfMinimized();
        }
    }

    public void ReloadVisuals(LauncherConfiguration configuration)
    {
        foreach (var (category, window) in _windows)
        {
            window.ReloadVisuals(category.ThemeOverride ?? configuration.Theme);
        }
    }

    public void Refresh(
        LauncherConfiguration configuration,
        Action<MenuCategory> onLayoutChanged,
        Action<MenuCategory> onDeleteRequested,
        IDesktopGroupCommands? commands = null)
    {
        _configuration = configuration;
        _save = onLayoutChanged;
        var groups = FindDesktopGroups(configuration).Where(g => !g.IsClosed).ToList();

        foreach (var stale in _windows.Keys.Except(groups).ToList())
        {
            _windows[stale].Close();
            _windows.Remove(stale);
        }

        foreach (var category in groups)
        {
            if (_windows.ContainsKey(category))
            {
                continue;
            }

            var theme = category.ThemeOverride ?? configuration.Theme;
            void Execute(LaunchItem item)
            {
                if (item.Type == LaunchItemType.GroupLink)
                {
                    commands?.FocusGroup(item.Target);
                }
                else
                {
                    LaunchExecutor.Execute(item);
                }
            }

            var window = new DesktopGroupWindow(category, theme, iconCache, Execute, onLayoutChanged, onDeleteRequested, commands);
            window.Closed += (_, _) => OnWindowClosed(category, window);
            window.Show();
            _windows[category] = window;
        }

        EnsureGroupsReachable();
        RelayoutDockWhenReady();
    }

    /// <summary>A group closed by its own "×" leaves the stack at once, instead of leaving a gap.</summary>
    private void OnWindowClosed(MenuCategory category, DesktopGroupWindow window)
    {
        if (_windows.TryGetValue(category, out var current) && ReferenceEquals(current, window))
        {
            _windows.Remove(category);
            if (IsDocked)
            {
                RelayoutDockWhenReady();
            }
        }
    }

    // ───────────────────────────── docking

    public bool IsDocked => _configuration?.Dock.IsDocked == true;

    /// <summary>
    /// Stacks every open group in one column starting where <paramref name="anchor"/> is, all
    /// collapsed, after remembering exactly how each one was shown.
    /// </summary>
    public void Dock(MenuCategory anchor)
    {
        if (_configuration is null || IsDocked)
        {
            return;
        }

        EnsureIds(_configuration);
        var members = _windows.Keys
            .OrderBy(group => group.DesktopY)
            .ThenBy(group => group.DesktopX)
            .ToList();

        var anchorWidth = anchor.DisplayMode == DesktopGroupDisplayMode.AppFolder
            ? anchor.DesktopWidth
            : _windows.TryGetValue(anchor, out var anchorWindow) && anchorWindow.ActualWidth > 0 ? anchorWindow.ActualWidth : anchor.DesktopWidth;

        _configuration.Dock = new DockState
        {
            IsDocked = true,
            Left = anchor.DesktopX,
            Top = anchor.DesktopY,
            Width = Math.Max(160, anchorWidth),
            Order = [.. members.Select(group => group.Id!)],
            Saved = [.. members.Select(GroupPlacement.From)]
        };

        RelayoutDock(animate: false);
    }

    /// <summary>Puts every docked group back exactly as it was when the stack was made.</summary>
    public void Undock()
    {
        if (_configuration is null || !IsDocked)
        {
            return;
        }

        var dock = _configuration.Dock;
        _configuration.Dock = new DockState();
        foreach (var placement in dock.Saved)
        {
            var group = FindGroupById(_configuration, placement.Id);
            if (group is null)
            {
                continue;
            }

            placement.ApplyTo(group);
            if (_windows.TryGetValue(group, out var window))
            {
                window.LeaveDock();
            }
        }

        if ((_windows.Keys.FirstOrDefault() ?? AllDesktopGroups(_configuration).FirstOrDefault()) is { } any)
        {
            _save?.Invoke(any);
        }

        EnsureGroupsReachable();
    }

    public DesktopGroupDisplayMode DockedStyle(MenuCategory member)
    {
        var saved = _configuration?.Dock.Saved.FirstOrDefault(placement => placement.Id == member.Id);
        return saved?.DisplayMode ?? member.DisplayMode;
    }

    public void DockToggleStyle(MenuCategory member)
    {
        var saved = _configuration?.Dock.Saved.FirstOrDefault(placement => placement.Id == member.Id);
        if (saved is null)
        {
            return;
        }

        saved.DisplayMode = saved.DisplayMode == DesktopGroupDisplayMode.AppFolder
            ? DesktopGroupDisplayMode.Panel
            : DesktopGroupDisplayMode.AppFolder;
        _save?.Invoke(member);
    }

    /// <summary>Opens <paramref name="member"/> and closes the one that was open — or closes it when it was already open.</summary>
    public void ToggleDockExpanded(MenuCategory member)
    {
        if (_configuration is null || !IsDocked)
        {
            return;
        }

        var dock = _configuration.Dock;
        dock.ExpandedId = dock.ExpandedId == member.Id ? null : member.Id;
        RelayoutDock(animate: true);
    }

    /// <summary>
    /// While one docked group is being dragged, puts every other one where the stack puts it
    /// relative to the dragged one — same left edge, each below the one above — without saving.
    /// </summary>
    public void DockFollow(MenuCategory member, double left, double top)
    {
        if (_configuration is null || !IsDocked)
        {
            return;
        }

        var ordered = _configuration.Dock.Order
            .Select(id => _windows.FirstOrDefault(pair => pair.Key.Id == id))
            .Where(pair => pair.Key is not null)
            .ToList();

        var offset = 0.0;
        foreach (var pair in ordered)
        {
            if (ReferenceEquals(pair.Key, member))
            {
                break;
            }

            offset += pair.Value.ActualHeight;
        }

        var y = top - offset;
        foreach (var pair in ordered)
        {
            if (!ReferenceEquals(pair.Key, member))
            {
                pair.Value.HoldPlacement(TimeSpan.FromSeconds(1));
                pair.Value.PlaceWithoutSaving(left, y);
            }

            y += pair.Value.ActualHeight;
        }
    }

    /// <summary>A docked group was dragged to (<paramref name="left"/>, <paramref name="top"/>): the stack follows it.</summary>
    public void DockMovedTo(MenuCategory member, double left, double top)
    {
        if (_configuration is null || !IsDocked)
        {
            return;
        }

        var dock = _configuration.Dock;
        var offset = 0.0;
        foreach (var id in dock.Order)
        {
            if (id == member.Id)
            {
                break;
            }

            if (_windows.Keys.FirstOrDefault(g => g.Id == id) is { } above && _windows.TryGetValue(above, out var window))
            {
                offset += window.ActualHeight;
            }
        }

        dock.Left = left;
        dock.Top = top - offset;
        RelayoutDock(animate: false);
    }

    /// <summary>Carries the stack into another work area, keeping it near the top and centred across.</summary>
    public void MoveDockInto(Rect workArea)
    {
        if (_configuration is null || !IsDocked)
        {
            return;
        }

        var dock = _configuration.Dock;
        dock.Left = workArea.Left + Math.Max(0, (workArea.Width - dock.Width) / 2);
        dock.Top = workArea.Top + 24;
        RelayoutDock(animate: false);
    }

    /// <summary>After a rebuild the title bars have no height until WPF has laid them out; wait for that.</summary>
    public void RelayoutDockWhenReady()
    {
        if (!IsDocked)
        {
            return;
        }

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            () => RelayoutDock(animate: false),
            DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Places every open group in its slot. Groups opened or created while docked join at the
    /// bottom (their current look saved first, so undocking returns them too); closed ones are
    /// skipped but keep their place in the order and their saved look.
    /// </summary>
    public void RelayoutDock(bool animate)
    {
        if (_configuration is null || !IsDocked || _windows.Count == 0)
        {
            return;
        }

        var dock = _configuration.Dock;
        foreach (var joining in _windows.Keys.Where(g => g.Id is not null && !dock.Order.Contains(g.Id)).OrderBy(g => g.DesktopY).ToList())
        {
            dock.Order.Add(joining.Id!);
            dock.Saved.Add(GroupPlacement.From(joining));
        }

        // Forget groups that no longer exist at all (deleted), not ones that are only closed.
        dock.Order.RemoveAll(id => FindGroupById(_configuration, id) is null);
        dock.Saved.RemoveAll(placement => FindGroupById(_configuration, placement.Id) is null);

        var open = dock.Order
            .Select(id => _windows.FirstOrDefault(pair => pair.Key.Id == id))
            .Where(pair => pair.Key is not null)
            .ToList();
        if (open.Count == 0)
        {
            return;
        }

        if (dock.ExpandedId is { } expandedId && open.All(pair => pair.Key.Id != expandedId))
        {
            dock.ExpandedId = null;
        }

        var areas = DisplayInventory.WorkAreas(open[0].Value);
        var area = areas.Count == 0
            ? SystemParameters.WorkArea
            : areas[Math.Clamp(MonitorPlacement.IndexOfOwner(new Rect(dock.Left, dock.Top, dock.Width, 1), areas), 0, areas.Count - 1)];

        // Keep the column on its monitor across.
        dock.Left = Math.Clamp(dock.Left, area.Left, Math.Max(area.Left, area.Right - dock.Width));

        var members = open
            .Select(pair => new DockMember(
                pair.Key.Id!,
                pair.Value.HeaderHeight,
                dock.Saved.FirstOrDefault(saved => saved.Id == pair.Key.Id)?.Height ?? pair.Key.DesktopHeight))
            .ToList();

        var (top, slots) = DockLayout.Arrange(members, dock.ExpandedId, dock.Top, area.Top, area.Bottom);
        dock.Top = top;

        foreach (var slot in slots)
        {
            var window = open.First(pair => pair.Key.Id == slot.Id).Value;
            window.HoldPlacement(TimeSpan.FromSeconds(1));
            window.ApplyDockGeometry(dock.Left, slot.Top, dock.Width, slot.Height, slot.Expanded, animate);
        }

        _save?.Invoke(open[0].Key);
    }

    public void OpenAllGroups(LauncherConfiguration configuration)
    {
        foreach (var group in AllDesktopGroups(configuration))
        {
            group.IsClosed = false;
        }
    }

    public void CloseAllGroups(LauncherConfiguration configuration)
    {
        foreach (var group in AllDesktopGroups(configuration))
        {
            group.IsClosed = true;
        }
    }

    public void ToggleGroup(MenuCategory group)
    {
        group.IsClosed = !group.IsClosed;
    }

    public void CloseAll()
    {
        foreach (var window in _windows.Values)
        {
            window.Close();
        }

        _windows.Clear();
    }

    public bool HasOpenGroups => _windows.Count > 0;

    /// <summary>The open window of a group, or null when the group is closed.</summary>
    public DesktopGroupWindow? WindowFor(MenuCategory group) => _windows.GetValueOrDefault(group);

    public void ToggleCollapseAll()
    {
        if (IsDocked)
        {
            // Docked, "all expanded" does not exist: this closes the one that is open.
            _configuration!.Dock.ExpandedId = null;
            RelayoutDock(animate: true);
            return;
        }

        if (_windows.Count == 0)
        {
            return;
        }

        var shouldCollapse = _windows.Values.Any(window => !window.IsCollapsed);
        foreach (var window in _windows.Values)
        {
            window.SetCollapsedExternally(shouldCollapse);
        }
    }

    public void GatherAll()
    {
        if (IsDocked)
        {
            MoveDockInto(SystemParameters.WorkArea);
            return;
        }

        if (_windows.Count == 0)
        {
            return;
        }

        var workArea = System.Windows.SystemParameters.WorkArea;
        var centerX = workArea.Left + (workArea.Width / 2);
        var centerY = workArea.Top + (workArea.Height / 2);
        var offset = 0.0;

        foreach (var window in _windows.Values)
        {
            window.MoveToCenterKeepingSize(centerX, centerY, offset);
            offset += 24;
        }
    }

    /// <summary>The list <paramref name="target"/> lives in, so a copy can join it.</summary>
    public static List<MenuCategory>? FindParentList(IMenuContainer container, MenuCategory target)
    {
        if (container.Categories.Contains(target))
        {
            return container.Categories;
        }

        foreach (var category in container.Categories)
        {
            var found = FindParentList(category, target);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Every desktop group in the configuration, whatever its nesting.</summary>
    public static IEnumerable<MenuCategory> AllDesktopGroups(IMenuContainer container)
    {
        foreach (var category in container.Categories)
        {
            if (category.IsDesktopGroup)
            {
                yield return category;
            }

            foreach (var nested in AllDesktopGroups(category))
            {
                yield return nested;
            }
        }
    }

    /// <summary>Gives every desktop group that lacks one a stable <see cref="MenuCategory.Id"/>. True when anything changed.</summary>
    public static bool EnsureIds(IMenuContainer container)
    {
        var changed = false;
        foreach (var group in AllDesktopGroups(container))
        {
            if (string.IsNullOrEmpty(group.Id))
            {
                group.Id = Guid.NewGuid().ToString("N");
                changed = true;
            }
        }

        return changed;
    }

    public static MenuCategory? FindGroupById(IMenuContainer container, string id)
    {
        return AllDesktopGroups(container).FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.Ordinal));
    }

    /// <summary>Brings an open group's window to the front with a short pulse. False when it has no window (closed).</summary>
    public bool FocusOpenGroup(MenuCategory group)
    {
        if (!_windows.TryGetValue(group, out var window))
        {
            return false;
        }

        // Docked, a collapsed group is a title bar in the stack: showing it means opening it there.
        if (IsDocked && _configuration!.Dock.ExpandedId != group.Id)
        {
            _configuration.Dock.ExpandedId = group.Id;
            RelayoutDock(animate: true);
        }

        window.BringForwardAndPulse();
        return true;
    }

    /// <summary>Removes every shortcut to <paramref name="groupId"/>, wherever it sits. True when any was removed.</summary>
    public static bool RemoveLinksTo(IMenuContainer container, string groupId)
    {
        var removed = false;
        foreach (var category in container.Categories)
        {
            removed |= category.Items.RemoveAll(i => i.Type == LaunchItemType.GroupLink && i.Target == groupId) > 0;
            removed |= RemoveLinksTo(category, groupId);
        }

        return removed;
    }

    public static bool RemoveCategory(IMenuContainer container, MenuCategory target)
    {
        if (container.Categories.Remove(target))
        {
            return true;
        }

        foreach (var category in container.Categories)
        {
            if (RemoveCategory(category, target))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<MenuCategory> FindDesktopGroups(IMenuContainer container)
    {
        foreach (var category in container.Categories)
        {
            if (category.IsDesktopGroup)
            {
                yield return category;
            }
        }
    }
}
