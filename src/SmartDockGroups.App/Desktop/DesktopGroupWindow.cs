using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Documents;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SmartDockGroups.App.Localization;
using SmartDockGroups.App.Menu;
using SmartDockGroups.App.Services;
using SmartDockGroups.App.Theming;
using SmartDockGroups.Core.Models;
using Application = System.Windows.Application;
using Border = System.Windows.Controls.Border;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Canvas = System.Windows.Controls.Canvas;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using ContextMenu = System.Windows.Controls.ContextMenu;
using Cursors = System.Windows.Input.Cursors;
using Dock = System.Windows.Controls.Dock;
using DockPanel = System.Windows.Controls.DockPanel;
using DragEventArgs = System.Windows.DragEventArgs;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Image = System.Windows.Controls.Image;
using ItemsControl = System.Windows.Controls.ItemsControl;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Keyboard = System.Windows.Input.Keyboard;
using MenuItem = System.Windows.Controls.MenuItem;
using ContentControl = System.Windows.Controls.ContentControl;
using Grid = System.Windows.Controls.Grid;
using MessageBox = System.Windows.MessageBox;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Orientation = System.Windows.Controls.Orientation;
using Panel = System.Windows.Controls.Panel;
using PlacementMode = System.Windows.Controls.Primitives.PlacementMode;
using Rectangle = System.Windows.Shapes.Rectangle;
using Separator = System.Windows.Controls.Separator;
using Slider = System.Windows.Controls.Slider;
using StackPanel = System.Windows.Controls.StackPanel;
using Style = System.Windows.Style;
using TextAlignment = System.Windows.TextAlignment;
using TextBlock = System.Windows.Controls.TextBlock;
using TextWrapping = System.Windows.TextWrapping;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace SmartDockGroups.App.Desktop;

internal sealed class DesktopGroupWindow : Window
{
    private const double MinIconScale = 0.5;
    private const double MaxIconScale = 3.0;
    private const double TileSize = 80;
    private const double MinCanvasPadding = 8;
    private const double CanvasPaddingRatio = 0.05;
    private const double FolderTapTolerance = 5;
    private const double MinGroupWidth = 140;
    private const double MinGroupHeight = 120;
    private const double ResizeEdgeThickness = 6;
    private const double ResizeCornerSize = 14;
    private const double AppFolderDesktopScale = 1.4;

    private readonly MenuCategory _category;
    private MenuTheme _theme;
    private readonly IconCacheService _iconCache;
    private readonly Action<LaunchItem> _onExecute;
    private readonly Action<MenuCategory> _onLayoutChanged;
    private readonly Action<MenuCategory> _onDeleteRequested;
    private readonly IDesktopGroupCommands? _commands;
    private readonly ScaleTransform _zoomTransform;
    private readonly Dictionary<MenuCategory, DesktopGroupWindow> _openSubfolders = new();

    /// <summary>Every open desktop group, so one can find another under a drag's drop point.</summary>
    private static readonly List<DesktopGroupWindow> _allGroupWindows = new();
    private readonly Dictionary<object, FrameworkElement> _tilesByEntry = new();
    private readonly HashSet<object> _selectedEntries = new();
    private readonly Dictionary<int, System.Windows.Point> _monitorPositions = new();

    private System.Windows.Point _marqueeStart;
    private bool _isMarqueeActive;
    private Border? _marqueeBorder;
    private HashSet<object> _preMarqueeSelection = new();

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    private const uint GA_ROOT = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private static readonly IntPtr HWND_TOP = new IntPtr(0);
    private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    private Canvas _canvas = null!;
    private System.Windows.Controls.ScrollViewer _scroller = null!;
    private Border _border = null!;
    private Grid _root = null!;
    private ContentControl _folderHost = null!;
    private GroupOverlayWindow? _overlay;
    private Border _header = null!;
    private readonly List<Border> _resizeHandles = new();
    private TextBlock _headerText = null!;
    private Border _collapseGlyph = null!;
    private Border _closeButton = null!;
    private Border _menuButton = null!;
    private readonly Dictionary<object, TextBlock> _captionsByEntry = new();
    private readonly Dictionary<object, Action> _openActionsByEntry = new();
    private object? _selectionAnchor;

    private Border _searchBar = null!;
    private System.Windows.Controls.TextBox _searchBox = null!;
    private TextBlock? _searchPlaceholder;
    private TextBlock _searchCountText = null!;
    private System.Windows.Controls.Primitives.Popup _searchPopup = null!;
    private System.Windows.Controls.ListBox _searchResultsList = null!;
    private readonly List<object> _searchMatches = new();
    private string _lastSearchText = string.Empty;
    private string _typeAheadBuffer = string.Empty;
    private DateTime _typeAheadLastInput;
    private static readonly TimeSpan TypeAheadTimeout = TimeSpan.FromSeconds(1);

    private ResizeEdge _resizeEdge = ResizeEdge.None;
    private System.Windows.Point? _placedAt;
    private DateTime _holdPlacementUntil;
    private DispatcherTimer? _persistMoveTimer;
    private System.Windows.Point _resizeOrigin;
    private double _resizeStartLeft;
    private double _resizeStartTop;
    private double _resizeStartWidth;
    private double _resizeStartHeight;

    private enum ResizeEdge
    {
        None,
        N,
        S,
        E,
        W,
        NE,
        NW,
        SE,
        SW
    }

    public DesktopGroupWindow(
        MenuCategory category,
        MenuTheme theme,
        IconCacheService iconCache,
        Action<LaunchItem> onExecute,
        Action<MenuCategory> onLayoutChanged,
        Action<MenuCategory> onDeleteRequested,
        IDesktopGroupCommands? commands = null)
    {
        _commands = commands;
        _category = category;
        _theme = theme;
        _iconCache = iconCache;
        _onExecute = onExecute;
        _onLayoutChanged = onLayoutChanged;
        _onDeleteRequested = onDeleteRequested;
        _zoomTransform = new ScaleTransform(category.DesktopIconScale, category.DesktopIconScale);

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = false;

        // Never drawn (no system title bar), but it names the window for screen readers,
        // UI Automation and the verification probe.
        Title = category.Name;
        Background = Brushes.Transparent;
        Left = category.DesktopX;
        Top = category.DesktopY;
        Width = category.DesktopWidth;
        Height = category.DesktopHeight;
        AllowDrop = true;

        Content = BuildRoot();
        SetCollapsed(_category.IsCollapsed);

        PreviewMouseWheel += OnPreviewMouseWheel;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewTextInput += OnPreviewTextInput;
        LocationChanged += OnLocationChanged;
        Deactivated += (_, _) => CloseFindOverlay(rememberQuery: true);
        Drop += OnDrop;
        _allGroupWindows.Add(this);
        Closed += (_, _) =>
        {
            _overlay?.Close();
            _pendingCuts.RemoveAll(p => ReferenceEquals(p.Owner, this));
            StopPendingCutWatcherIfIdle();
            _allGroupWindows.Remove(this);
        };
    }

    public bool IsCollapsed => _category.IsCollapsed;

    internal MenuCategory Category => _category;

    /// <summary>
    /// True while this group is part of the docked stack (<see cref="DesktopOrganizerService"/>
    /// owns its position, size and collapsed state then): the chevron asks the stack to expand
    /// it, dragging the title moves the whole stack, and the resize edges are off.
    /// </summary>
    private bool _isDockedMember;

    internal bool IsDockedMember => _isDockedMember;

    /// <summary>Height of the group shown as just its title bar, border included.</summary>
    internal double HeaderHeight => _header.ActualHeight > 0
        ? _header.ActualHeight + 2
        : _theme.TitleFontSize + 24;

    /// <summary>
    /// Puts the group in its slot of the docked stack. The slot becomes the group's own saved
    /// position and size, so everything that already reads them (restoring after a monitor
    /// change, persisting) keeps working; what the group looked like before docking lives in
    /// <see cref="DockState.Saved"/>. Groups below an expanding one slide instead of jumping.
    /// </summary>
    internal void ApplyDockGeometry(double left, double top, double width, double height, bool expanded, bool animate)
    {
        _isDockedMember = true;
        if (IsAppFolder)
        {
            _overlay?.Close();
            _category.DisplayMode = DesktopGroupDisplayMode.Panel;
            _folderHost.Visibility = Visibility.Collapsed;
            _border.Visibility = Visibility.Visible;
        }

        var widthChanged = Math.Abs(_category.DesktopWidth - width) > 0.5;
        _category.IsCollapsed = !expanded;
        _scroller.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        UpdateCollapseGlyph();
        SetResizeHandlesVisible(false);

        SizeToContent = SizeToContent.Manual;
        Width = width;
        Height = height;
        _category.DesktopWidth = width;
        if (expanded)
        {
            _category.DesktopHeight = height;
        }

        _category.DesktopX = left;
        _category.DesktopY = top;
        _placedAt = new System.Windows.Point(left, top);
        Left = left;
        if (animate && Math.Abs(Top - top) > 0.5)
        {
            var slide = new DoubleAnimation(Top, top, TimeSpan.FromMilliseconds(Math.Max(_theme.AnimationDurationMs, 1) * 1.4))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            slide.Completed += (_, _) =>
            {
                BeginAnimation(TopProperty, null);
                Top = top;
            };
            BeginAnimation(TopProperty, slide);
        }
        else
        {
            BeginAnimation(TopProperty, null);
            Top = top;
        }

        if (widthChanged && _category.IconArrangement != IconArrangement.None)
        {
            ArrangeInGrid(NameOrder());
        }
        else
        {
            UpdateCanvasExtent();
        }
    }

    /// <summary>Leaves the stack: the group shows itself as its (just restored) category says.</summary>
    internal void LeaveDock()
    {
        _isDockedMember = false;
        BeginAnimation(TopProperty, null);
        PlaceWithoutSaving(_category.DesktopX, _category.DesktopY);
        SetCollapsed(_category.IsCollapsed);
        FinishStructuralChange();
    }

    /// <summary>
    /// Brings this group back after Windows hid it, and does the same for any open
    /// subfolders. Measured directly rather than assumed: Show Desktop does not minimize
    /// a window with no taskbar button — WindowState stays Normal — it just brings the
    /// desktop's own window to the front of the z-order and leaves ours buried under it.
    /// Toggling Topmost is the standard way to force a window back to the front of its
    /// z-order band without stealing focus permanently. The WindowState check stays as
    /// a second line of defence, in case some other trigger does minimize it for real.
    /// </summary>
    internal void RestoreIfMinimized()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Topmost = true;
        Topmost = false;

        foreach (var subfolder in _openSubfolders.Values)
        {
            subfolder.RestoreIfMinimized();
        }
    }

    /// <summary>
    /// What a taskbar shortcut asks for: make sure the group is visible (expanded, in front of
    /// the windows above it, focused) and pulse it briefly so the eye finds it.
    /// </summary>
    internal void BringForwardAndPulse()
    {
        if (_category.IsCollapsed)
        {
            SetCollapsedExternally(false);
        }

        RestoreIfMinimized();
        Activate();

        if (!IsAppFolder)
        {
            // Landing on a group by its shortcut puts the keyboard on its first icon.
            SelectFirstVisual();
        }

        if (Content is UIElement root)
        {
            var pulse = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.55, TimeSpan.FromMilliseconds(180))
            {
                AutoReverse = true,
                RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(2)
            };
            pulse.Completed += (_, _) => root.BeginAnimation(OpacityProperty, null);
            root.BeginAnimation(OpacityProperty, pulse);
        }
    }

    /// <summary>
    /// Where the group lives by choice, at its current size. It can differ from where
    /// the window is: a group rescued off a monitor that went away keeps this as its
    /// home, so it can walk back when the monitor returns.
    /// </summary>
    internal Rect HomeRect => new(
        _category.DesktopX,
        _category.DesktopY,
        ActualWidth > 0 ? ActualWidth : _category.DesktopWidth,
        ActualHeight > 0 ? ActualHeight : _category.DesktopHeight);

    /// <summary>
    /// Pulls the whole window back inside one monitor's work area, on all four sides. After a drag
    /// that monitor is the one under the mouse when the button was released — a group dropped
    /// across two monitors goes to the one the user was pointing at; otherwise it is the monitor
    /// holding most of the window. A group larger than the monitor keeps its top-left corner in view.
    /// </summary>
    private void KeepOnScreen(bool byCursor)
    {
        var areas = DisplayInventory.WorkAreas(this);
        if (areas.Count == 0)
        {
            return;
        }

        var size = _category.IsCollapsed || IsAppFolder || _isDockedMember
            ? new System.Windows.Size(ActualWidth > 0 ? ActualWidth : _category.DesktopWidth, ActualHeight > 0 ? ActualHeight : _category.DesktopHeight)
            : new System.Windows.Size(_category.DesktopWidth, _category.DesktopHeight);

        int index;
        if (byCursor && MonitorUnderCursor() is { } deviceName)
        {
            index = Array.FindIndex(System.Windows.Forms.Screen.AllScreens, candidate => candidate.DeviceName == deviceName);
        }
        else
        {
            index = MonitorPlacement.IndexOfOwner(new Rect(Left, Top, size.Width, size.Height), areas);
        }

        var area = areas[Math.Clamp(index, 0, areas.Count - 1)];
        var corrected = MonitorPlacement.ClampInto(new System.Windows.Point(Left, Top), size, area);
        if (Math.Abs(corrected.X - Left) > 0.5 || Math.Abs(corrected.Y - Top) > 0.5)
        {
            PlaceWithoutSaving(corrected.X, corrected.Y);
        }
    }

    /// <summary>
    /// The device name of the monitor the mouse is on. Asked in the per-monitor DPI context on
    /// purpose: this process is system-DPI aware, and with monitors of different scales its own
    /// view of the cursor and of the monitors disagree (measured 2026-09-30: the cursor at
    /// x=3915 on the second monitor was resolved to the first one). Physical pixels do not.
    /// </summary>
    private static string? MonitorUnderCursor()
    {
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4)); // PER_MONITOR_AWARE_V2
        try
        {
            if (!GetCursorPos(out var cursor))
            {
                return null;
            }

            var monitor = MonitorFromPoint(cursor, 2 /* MONITOR_DEFAULTTONEAREST */);
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            return monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info) ? info.szDevice : null;
        }
        finally
        {
            if (previous != IntPtr.Zero)
            {
                SetThreadDpiAwarenessContext(previous);
            }
        }
    }

    /// <summary>Moves the window without recording the move as the group's new home.</summary>
    internal void PlaceWithoutSaving(double left, double top)
    {
        // Remembered rather than flagged: WPF reports the move after this method has
        // returned, so a "moving now" flag is already clear by the time anyone checks it.
        _placedAt = new System.Windows.Point(left, top);
        Left = left;
        Top = top;
    }

    /// <summary>
    /// Ignores moves for a while. Windows shuffles windows on its own while monitors come
    /// and go, and remembering those shuffles would overwrite the group's real home.
    /// </summary>
    internal void HoldPlacement(TimeSpan duration)
    {
        _holdPlacementUntil = DateTime.UtcNow + duration;
    }

    /// <summary>True while the user drags this docked group by its title: the rest of the stack follows live.</summary>
    private bool _dockDragging;

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (_dockDragging && _commands is not null)
        {
            _commands.DockFollow(_category, Left, Top);
            return;
        }

        if (DateTime.UtcNow < _holdPlacementUntil)
        {
            return;
        }

        if (_persistMoveTimer is null)
        {
            _persistMoveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _persistMoveTimer.Tick += (_, _) =>
            {
                _persistMoveTimer.Stop();
                PersistExternalMove();
            };
        }

        // Debounced: a drag reports dozens of positions and only the last one matters.
        _persistMoveTimer.Stop();
        _persistMoveTimer.Start();
    }

    /// <summary>
    /// Remembers a move this class did not make itself — Windows' own Win+Shift+arrow,
    /// for instance, when the shell handles the shortcut before the group ever sees it.
    /// </summary>
    private void PersistExternalMove()
    {
        if (DateTime.UtcNow < _holdPlacementUntil)
        {
            return;
        }

        // Still exactly where this class put it: that was a placement, not a user's move.
        if (_placedAt is { } placed && Math.Abs(Left - placed.X) < 0.5 && Math.Abs(Top - placed.Y) < 0.5)
        {
            return;
        }

        if (Math.Abs(Left - _category.DesktopX) < 0.5 && Math.Abs(Top - _category.DesktopY) < 0.5)
        {
            return;
        }

        _category.DesktopX = Left;
        _category.DesktopY = Top;
        _monitorPositions.Clear();
        _onLayoutChanged(_category);
    }

    /// <summary>
    /// Carries the group to the monitor beside the one it is on, keeping its relative
    /// place. With a single monitor there is nowhere to go and nothing happens.
    /// </summary>
    private void MoveToAdjacentMonitor(int direction)
    {
        var areas = DisplayInventory.WorkAreas(this);
        var current = new Rect(Left, Top, ActualWidth, ActualHeight);
        var from = MonitorPlacement.IndexOfOwner(current, areas);

        if (MonitorPlacement.AdjacentIndex(from, areas, direction) is not { } to)
        {
            return;
        }

        _monitorPositions[from] = new System.Windows.Point(Left, Top);

        System.Windows.Point position;
        if (_monitorPositions.TryGetValue(to, out var remembered)
            && MonitorPlacement.IsReachable(new Rect(remembered.X, remembered.Y, ActualWidth, ActualHeight), areas))
        {
            position = remembered;
        }
        else
        {
            position = MonitorPlacement.MapBetween(current, areas[from], areas[to]);
            _monitorPositions[to] = position;
        }

        if (_isDockedMember && _commands is not null)
        {
            _commands.DockMovedTo(_category, position.X, position.Y);
            return;
        }

        PlaceWithoutSaving(position.X, position.Y);

        _category.DesktopX = position.X;
        _category.DesktopY = position.Y;
        _onLayoutChanged(_category);
    }

    public void SetCollapsedExternally(bool collapsed)
    {
        if (_isDockedMember)
        {
            return;
        }

        SetCollapsed(collapsed);
        _onLayoutChanged(_category);
    }

    public void MoveToCenterKeepingSize(double centerX, double centerY, double cascadeOffset)
    {
        Left = centerX - (_category.DesktopWidth / 2) + cascadeOffset;
        Top = centerY - (_category.DesktopHeight / 2) + cascadeOffset;
        _category.DesktopX = Left;
        _category.DesktopY = Top;
        _onLayoutChanged(_category);
    }

    /// <summary>
    /// Both looks are built up front and swapped by visibility, so the panel canvas,
    /// header and grip stay alive (and keep working) while the folder look is showing.
    /// </summary>
    private FrameworkElement BuildRoot()
    {
        _folderHost = new ContentControl
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(10, 10, 10, 6),
            Visibility = Visibility.Collapsed
        };
        _folderHost.MouseLeftButtonDown += OnFolderTileMouseDown;
        _folderHost.PreviewMouseRightButtonDown += (_, _) => _folderHost.ContextMenu = BuildHeaderContextMenu();

        _root = new Grid { Background = Brushes.Transparent };
        _root.Children.Add(BuildContent());
        _root.Children.Add(_folderHost);
        _root.Children.Add(_searchPopup);
        BuildResizeHandles(_root);
        return _root;
    }

    /// <summary>
    /// Eight thin, invisible strips laid over the window's own edges and corners so the
    /// whole perimeter resizes, the way a normal titled window does — not just the one
    /// corner a single grip could reach. They sit on top (added last) so they win the
    /// hit test over the header or canvas in that last handful of pixels.
    /// </summary>
    private void BuildResizeHandles(Grid root)
    {
        _resizeHandles.Clear();

        AddResizeHandle(root, ResizeEdge.N, Cursors.SizeNS,
            HorizontalAlignment.Stretch, VerticalAlignment.Top,
            double.NaN, ResizeEdgeThickness,
            new Thickness(ResizeCornerSize, 0, ResizeCornerSize, 0));
        AddResizeHandle(root, ResizeEdge.S, Cursors.SizeNS,
            HorizontalAlignment.Stretch, VerticalAlignment.Bottom,
            double.NaN, ResizeEdgeThickness,
            new Thickness(ResizeCornerSize, 0, ResizeCornerSize, 0));
        AddResizeHandle(root, ResizeEdge.W, Cursors.SizeWE,
            HorizontalAlignment.Left, VerticalAlignment.Stretch,
            ResizeEdgeThickness, double.NaN,
            new Thickness(0, ResizeCornerSize, 0, ResizeCornerSize));
        AddResizeHandle(root, ResizeEdge.E, Cursors.SizeWE,
            HorizontalAlignment.Right, VerticalAlignment.Stretch,
            ResizeEdgeThickness, double.NaN,
            new Thickness(0, ResizeCornerSize, 0, ResizeCornerSize));

        AddResizeHandle(root, ResizeEdge.NW, Cursors.SizeNWSE,
            HorizontalAlignment.Left, VerticalAlignment.Top,
            ResizeCornerSize, ResizeCornerSize, new Thickness(0));
        AddResizeHandle(root, ResizeEdge.SE, Cursors.SizeNWSE,
            HorizontalAlignment.Right, VerticalAlignment.Bottom,
            ResizeCornerSize, ResizeCornerSize, new Thickness(0));
        AddResizeHandle(root, ResizeEdge.NE, Cursors.SizeNESW,
            HorizontalAlignment.Right, VerticalAlignment.Top,
            ResizeCornerSize, ResizeCornerSize, new Thickness(0));
        AddResizeHandle(root, ResizeEdge.SW, Cursors.SizeNESW,
            HorizontalAlignment.Left, VerticalAlignment.Bottom,
            ResizeCornerSize, ResizeCornerSize, new Thickness(0));
    }

    private void AddResizeHandle(
        Grid root,
        ResizeEdge edge,
        System.Windows.Input.Cursor cursor,
        HorizontalAlignment horizontalAlignment,
        VerticalAlignment verticalAlignment,
        double width,
        double height,
        Thickness margin)
    {
        var handle = new Border
        {
            Background = Brushes.Transparent,
            Cursor = cursor,
            HorizontalAlignment = horizontalAlignment,
            VerticalAlignment = verticalAlignment,
            Margin = margin,
            // Matches the panel's own rounding so a corner handle's hit area follows the
            // curve instead of squaring it off — invisible either way, but it keeps the
            // cursor change lined up with where the eye reads the corner as starting.
            CornerRadius = new CornerRadius(_theme.CornerRadius)
        };

        if (!double.IsNaN(width))
        {
            handle.Width = width;
        }

        if (!double.IsNaN(height))
        {
            handle.Height = height;
        }

        handle.MouseLeftButtonDown += (sender, e) => OnResizeMouseDown(edge, (UIElement)sender, e);
        handle.MouseMove += OnResizeMouseMove;
        handle.MouseLeftButtonUp += OnResizeMouseUp;

        root.Children.Add(handle);
        _resizeHandles.Add(handle);
    }

    private bool IsAppFolder => _category.DisplayMode == DesktopGroupDisplayMode.AppFolder;

    /// <summary>
    /// Icons keep at least a twentieth of the group clear on every side, so nothing
    /// sits flush against the title bar or the border however the group is resized.
    /// </summary>
    private double PaddingX => Math.Max(MinCanvasPadding, _category.DesktopWidth * CanvasPaddingRatio);

    private double PaddingY => Math.Max(MinCanvasPadding, _category.DesktopHeight * CanvasPaddingRatio);

    /// <summary>Horizontal cell stride (pre-zoom): icon size plus the user-defined extra gap.</summary>
    private double HStride => TileSize + _category.IconHGap;

    /// <summary>Vertical cell stride (pre-zoom): icon size plus the user-defined extra gap.</summary>
    private double VStride => TileSize + _category.IconVGap;

    private void ApplyDisplayMode()
    {
        UpdateHeaderTooltip();

        if (IsAppFolder)
        {
            RefreshFolderTile();
            _border.Visibility = Visibility.Collapsed;
            _folderHost.Visibility = Visibility.Visible;
            SetResizeHandlesVisible(false);
            SizeToContent = SizeToContent.WidthAndHeight;
            return;
        }

        _folderHost.Visibility = Visibility.Collapsed;
        _border.Visibility = Visibility.Visible;
        SetResizeHandlesVisible(!_category.IsCollapsed);

        // SizeToContent has to be relaxed before Width and Height are assigned:
        // while it is still automatic the assignment is discarded, which is how the
        // panel used to come back from the folder look wearing the tile's size.
        SizeToContent = _category.IsCollapsed ? SizeToContent.Height : SizeToContent.Manual;
        Width = _category.DesktopWidth;

        if (!_category.IsCollapsed)
        {
            Height = _category.DesktopHeight;
        }
    }

    private void RefreshFolderTile()
    {
        if (!IsAppFolder)
        {
            return;
        }

        _folderHost.Content = AppFolderTile.Build(_category, _theme, _iconCache, AppFolderDesktopScale);
    }

    private void ToggleDisplayMode()
    {
        if (!IsAppFolder)
        {
            // Remember where the panel stood so the tile takes its top-left corner.
            _category.PanelX = Left;
            _category.PanelY = Top;
        }

        // The sheet belongs to the folder look; leaving it (possibly from the sheet's own menu) closes it.
        _overlay?.Close();
        _category.DisplayMode = IsAppFolder ? DesktopGroupDisplayMode.Panel : DesktopGroupDisplayMode.AppFolder;
        ApplyDisplayMode();
        _onLayoutChanged(_category);
    }

    /// <summary>
    /// Drives the whole press-drag-release gesture through WPF's own <see cref="Window.DragMove"/>
    /// — the same primitive the header already uses — instead of accumulating the move by
    /// hand from <c>PointToScreen</c> deltas. The hand-rolled version was the actual cause
    /// of the tile "disappearing": on a per-monitor-DPI-aware system, calling
    /// <c>PointToScreen</c> a second time in the same handler, right after the window's own
    /// position had just been changed by the first delta, could read back a value scaled by
    /// a different DPI factor than the first call — landing the window at a corrupted
    /// coordinate (observed once at exactly <c>Int16.MinValue</c>) with no monitor anywhere
    /// near it. <c>DragMove</c> hands the whole drag to Windows itself, immune to that.
    /// </summary>
    private void OnFolderTileMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        DragMove();
        e.Handled = true;

        // A press that barely moved is a tap, not a drag: snap back and open the sheet.
        if (Math.Abs(Left - _category.DesktopX) < FolderTapTolerance
            && Math.Abs(Top - _category.DesktopY) < FolderTapTolerance)
        {
            Left = _category.DesktopX;
            Top = _category.DesktopY;

            // Let this click finish before another window takes focus, otherwise the
            // captured mouse-up drags activation back here and the sheet closes at once.
            Dispatcher.BeginInvoke(OpenOverlay, DispatcherPriority.Input);
            return;
        }

        KeepOnScreen(byCursor: true);
        _category.DesktopX = Left;
        _category.DesktopY = Top;
        _monitorPositions.Clear();
        _onLayoutChanged(_category);
    }

    private void OpenOverlay()
    {
        if (_overlay is not null)
        {
            _overlay.Activate();
            return;
        }

        _overlay = new GroupOverlayWindow(_category, _theme, _iconCache, _onExecute, this);
        _overlay.Closed += (_, _) => _overlay = null;
        _overlay.Show();
        _overlay.Activate();
    }

    private FrameworkElement BuildContent()
    {
        var panel = new DockPanel();

        _headerText = new TextBlock
        {
            Text = _category.Name,
            Foreground = ThemeBrushes.CreateBrush(_theme.TextColor, 1.0),
            FontFamily = new FontFamily(_theme.TitleFontFamily),
            FontSize = _theme.TitleFontSize,
            FontWeight = _theme.TitleBold ? FontWeights.Bold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        _closeButton = BuildHeaderButton(CloseGroup);
        _closeButton.Child = AppIcons.Create("IconClose", ThemeBrushes.CreateBrush(_theme.TextColor, 1.0), _theme.TitleFontSize);
        _closeButton.ToolTip = LocalizationService.Get("group.close");
        DockPanel.SetDock(_closeButton, Dock.Right);

        _collapseGlyph = BuildHeaderButton(ToggleCollapse);
        DockPanel.SetDock(_collapseGlyph, Dock.Right);

        _menuButton = BuildHeaderButton(OpenHeaderMenu);
        _menuButton.Child = AppIcons.Create("IconMore", ThemeBrushes.CreateBrush(_theme.TextColor, 1.0), _theme.TitleFontSize + 3);
        _menuButton.ToolTip = LocalizationService.Get("group.menu");
        DockPanel.SetDock(_menuButton, Dock.Right);

        var headerPanel = new DockPanel();
        headerPanel.Children.Add(_closeButton);
        headerPanel.Children.Add(_menuButton);
        headerPanel.Children.Add(_collapseGlyph);
        headerPanel.Children.Add(_headerText);

        _header = new Border
        {
            Padding = new Thickness(8, 4, 4, 4),
            Child = headerPanel
        };
        _header.MouseLeftButtonDown += OnHeaderMouseLeftButtonDown;
        // Rebuilt fresh on every right-click rather than trusted to whichever action
        // last happened to reassign it — checkmarks (arrangement, icon size, badge) need
        // to reflect the group's actual current state, not whatever it was the last time
        // something else touched the menu.
        _header.PreviewMouseRightButtonDown += (_, _) => _header.ContextMenu = BuildHeaderContextMenu();
        DockPanel.SetDock(_header, Dock.Top);
        panel.Children.Add(_header);

        _searchBar = BuildSearchBar();
        DockPanel.SetDock(_searchBar, Dock.Top);
        panel.Children.Add(_searchBar);

        // A layout (not render) transform, so the scroller below sees the zoomed size and
        // scrolls exactly as far as the icons reach. Canvas coordinates stay pre-zoom either way.
        _canvas = new Canvas
        {
            Background = Brushes.Transparent,
            LayoutTransform = _zoomTransform
        };
        _canvas.MouseLeftButtonDown += (_, e) =>
        {
            CloseFindOverlay(rememberQuery: true);
            var isCtrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            var isShift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

            if (!isCtrl && !isShift)
            {
                ClearSelection();
                _preMarqueeSelection.Clear();
            }
            else
            {
                _preMarqueeSelection = new HashSet<object>(_selectedEntries);
            }

            _marqueeStart = e.GetPosition(_canvas);
            _isMarqueeActive = true;
            _canvas.CaptureMouse();

            if (_marqueeBorder is not null)
            {
                _canvas.Children.Remove(_marqueeBorder);
            }

            _marqueeBorder = new Border
            {
                BorderBrush = ThemeBrushes.CreateBrush(_theme.HighlightColor, 0.85),
                BorderThickness = new Thickness(1),
                Background = ThemeBrushes.CreateBrush(_theme.HighlightColor, 0.20),
                CornerRadius = new CornerRadius(2),
                IsHitTestVisible = false
            };

            Canvas.SetLeft(_marqueeBorder, _marqueeStart.X);
            Canvas.SetTop(_marqueeBorder, _marqueeStart.Y);
            _marqueeBorder.Width = 0;
            _marqueeBorder.Height = 0;
            _canvas.Children.Add(_marqueeBorder);

            e.Handled = true;
        };

        _canvas.MouseMove += (_, e) =>
        {
            if (!_isMarqueeActive || _marqueeBorder is null)
            {
                return;
            }

            var current = e.GetPosition(_canvas);
            var left = Math.Min(_marqueeStart.X, current.X);
            var top = Math.Min(_marqueeStart.Y, current.Y);
            var width = Math.Abs(current.X - _marqueeStart.X);
            var height = Math.Abs(current.Y - _marqueeStart.Y);

            Canvas.SetLeft(_marqueeBorder, left);
            Canvas.SetTop(_marqueeBorder, top);
            _marqueeBorder.Width = width;
            _marqueeBorder.Height = height;

            var marqueeRect = new Rect(left, top, width, height);
            var isCtrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

            foreach (var (entry, tile) in _tilesByEntry)
            {
                var tileLeft = Canvas.GetLeft(tile);
                var tileTop = Canvas.GetTop(tile);
                var tileWidth = tile.ActualWidth > 0 ? tile.ActualWidth : TileSize - 8;
                var tileHeight = tile.ActualHeight > 0 ? tile.ActualHeight : TileSize;
                var tileRect = new Rect(tileLeft, tileTop, tileWidth, tileHeight);

                var intersects = marqueeRect.IntersectsWith(tileRect);

                if (isCtrl)
                {
                    var originallySelected = _preMarqueeSelection.Contains(entry);
                    if (intersects)
                    {
                        if (originallySelected)
                        {
                            _selectedEntries.Remove(entry);
                        }
                        else
                        {
                            _selectedEntries.Add(entry);
                        }
                    }
                    else
                    {
                        if (originallySelected)
                        {
                            _selectedEntries.Add(entry);
                        }
                        else
                        {
                            _selectedEntries.Remove(entry);
                        }
                    }
                }
                else
                {
                    var originallySelected = _preMarqueeSelection.Contains(entry);
                    if (intersects || originallySelected)
                    {
                        _selectedEntries.Add(entry);
                    }
                    else
                    {
                        _selectedEntries.Remove(entry);
                    }
                }
            }

            RefreshSelectionVisuals();
        };

        _canvas.MouseLeftButtonUp += (_, e) =>
        {
            if (_isMarqueeActive)
            {
                _isMarqueeActive = false;
                _canvas.ReleaseMouseCapture();
                if (_marqueeBorder is not null)
                {
                    _canvas.Children.Remove(_marqueeBorder);
                    _marqueeBorder = null;
                }
                e.Handled = true;
            }
        };

        _canvas.LostMouseCapture += (_, _) =>
        {
            if (_isMarqueeActive)
            {
                var isLButtonDown = (GetAsyncKeyState(0x01) & 0x8000) != 0;
                if (!isLButtonDown)
                {
                    _isMarqueeActive = false;
                    if (_marqueeBorder is not null)
                    {
                        _canvas.Children.Remove(_marqueeBorder);
                        _marqueeBorder = null;
                    }
                }
            }
        };

        _canvas.PreviewMouseRightButtonDown += (_, _) => _canvas.ContextMenu = BuildHeaderContextMenu();

        _scroller = new System.Windows.Controls.ScrollViewer
        {
            Content = _canvas,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            Background = Brushes.Transparent,
            Focusable = false,
            // Arrow keys belong to icon navigation (OnPreviewKeyDown), not to scrolling.
            IsTabStop = false
        };
        _scroller.SizeChanged += (_, _) => UpdateCanvasExtent();

        // The stock template paints the square where both scrollbars meet with the system
        // control colour — a white block on a dark group. Only that square reads this key.
        _scroller.Resources[System.Windows.SystemColors.ControlBrushKey] = Brushes.Transparent;

        PopulateTiles();

        panel.Children.Add(_scroller);

        _border = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(_theme.CornerRadius),
            ClipToBounds = true,
            Child = panel
        };
        ApplyBackground();
        ApplyHeaderBackground();
        UpdateCollapseGlyph();

        _border.Effect = BuildGroupShadow(_theme);

        return _border;
    }

    private void CloseGroup()
    {
        _category.IsClosed = true;
        _onLayoutChanged(_category);
        Close();
    }

    /// <summary>
    /// A Ctrl+F search strip docked under the header, hidden until asked for. The match
    /// list itself lives in a <see cref="System.Windows.Controls.Primitives.Popup"/>
    /// anchored to this bar rather than in the DockPanel flow, so it floats over the
    /// icons instead of pushing them down.
    /// </summary>
    private Border BuildSearchBar()
    {
        var searchIcon = AppIcons.Create("IconSearch", ThemeBrushes.CreateBrush(_theme.TextColor, 0.65), _theme.ItemFontSize + 2);
        var iconWrap = new Border
        {
            Child = searchIcon,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        DockPanel.SetDock(iconWrap, Dock.Left);

        var closeSearch = new Border
        {
            Width = _theme.ItemFontSize + 4,
            Height = _theme.ItemFontSize + 4,
            CornerRadius = new CornerRadius(3),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
            Child = AppIcons.Create("IconClose", ThemeBrushes.CreateBrush(_theme.TextColor, 0.65), _theme.ItemFontSize - 2),
            ToolTip = LocalizationService.Get("common.cancel")
        };
        closeSearch.MouseLeftButtonDown += (_, _) => CloseFindOverlay(rememberQuery: false);
        DockPanel.SetDock(closeSearch, Dock.Right);

        _searchCountText = new TextBlock
        {
            Foreground = ThemeBrushes.CreateBrush(_theme.TextColor, 0.65),
            FontFamily = new FontFamily(_theme.ItemFontFamily),
            FontSize = Math.Max(10, _theme.ItemFontSize - 1),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        DockPanel.SetDock(_searchCountText, Dock.Right);

        _searchPlaceholder = new TextBlock
        {
            Text = LocalizationService.Get("group.searchPlaceholder") + " (Esc)",
            Foreground = ThemeBrushes.CreateBrush(_theme.TextColor, 0.4),
            FontFamily = new FontFamily(_theme.ItemFontFamily),
            FontSize = _theme.ItemFontSize,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        _searchBox = new System.Windows.Controls.TextBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = ThemeBrushes.CreateBrush(_theme.TextColor, 1.0),
            FontFamily = new FontFamily(_theme.ItemFontFamily),
            FontSize = _theme.ItemFontSize,
            VerticalAlignment = VerticalAlignment.Center
        };
        _searchBox.TextChanged += (_, _) =>
        {
            if (_searchPlaceholder is not null)
            {
                _searchPlaceholder.Visibility = string.IsNullOrEmpty(_searchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            }
            UpdateSearchMatches(_searchBox.Text);
        };
        _searchBox.LostFocus += (_, _) =>
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
            {
                if (!_searchBox.IsKeyboardFocusWithin && !_searchResultsList.IsKeyboardFocusWithin)
                {
                    CloseFindOverlay(rememberQuery: true);
                }
            });
        };

        var boxGrid = new Grid();
        boxGrid.Children.Add(_searchPlaceholder);
        boxGrid.Children.Add(_searchBox);

        var row = new DockPanel();
        row.Children.Add(iconWrap);
        row.Children.Add(closeSearch);
        row.Children.Add(_searchCountText);
        row.Children.Add(boxGrid);

        var bar = new Border
        {
            Padding = new Thickness(8, 4, 8, 4),
            Visibility = Visibility.Collapsed,
            Child = row
        };
        ApplySearchBarAppearance(bar);

        _searchResultsList = new System.Windows.Controls.ListBox
        {
            MaxHeight = 240,
            Background = ThemeBrushes.CreateBrush(_theme.BackgroundColor, 0.98),
            BorderBrush = ThemeBrushes.CreateBrush(_theme.BorderColor, 1.0),
            BorderThickness = new Thickness(1)
        };

        _searchPopup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = bar,
            Placement = PlacementMode.Bottom,
            StaysOpen = true,
            AllowsTransparency = true,
            Child = _searchResultsList
        };
        bar.SizeChanged += (_, _) => _searchPopup.Width = bar.ActualWidth;

        return bar;
    }

    private void ApplySearchBarAppearance(Border bar)
    {
        bar.Background = ThemeBrushes.CreateBrush(_theme.HighlightColor, 0.18);
        bar.BorderBrush = ThemeBrushes.CreateBrush(_theme.BorderColor, 1.0);
        bar.BorderThickness = new Thickness(0, 0, 0, 1);
    }

    /// <summary>Only meaningful in the free-canvas panel look — the closed folder tile has no icons to search.</summary>
    private void OpenFindOverlay()
    {
        if (IsAppFolder)
        {
            return;
        }

        _searchBar.Visibility = Visibility.Visible;
        _searchBox.Text = _lastSearchText;
        if (_searchPlaceholder is not null)
        {
            _searchPlaceholder.Visibility = string.IsNullOrEmpty(_searchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        }
        _searchBox.SelectAll();
        _searchBox.Focus();
        Keyboard.Focus(_searchBox);
        UpdateSearchMatches(_searchBox.Text);
    }

    /// <summary>
    /// Remembers the query exactly at the moment the user picked a result or backed out
    /// with Esc — never at, say, a stray loss of focus — so the next Ctrl+F picks up
    /// where this one left off.
    /// </summary>
    private void CloseFindOverlay(bool rememberQuery)
    {
        if (_searchBar.Visibility != Visibility.Visible)
        {
            return;
        }

        if (rememberQuery)
        {
            _lastSearchText = _searchBox.Text;
        }

        _searchPopup.IsOpen = false;
        _searchBar.Visibility = Visibility.Collapsed;
        ClearAllHighlights();
        _searchMatches.Clear();
        _searchResultsList.Items.Clear();
        RefreshSelectionVisuals();
        RefreshCutVisuals();
        Keyboard.ClearFocus();
    }

    private bool IsSearchActive =>
        _searchBar is not null && _searchBar.Visibility == Visibility.Visible && !string.IsNullOrEmpty(_searchBox.Text);

    /// <summary>
    /// While Ctrl+F has a query, the icons themselves answer it — not only the name
    /// highlighting and the list under the box: every match gets a tinted backdrop, the
    /// one Enter would open gets the full highlight and is scrolled into view, and
    /// everything else fades back so the matches stand out at a glance.
    /// </summary>
    private void ReapplySearchEmphasis()
    {
        if (!IsSearchActive)
        {
            return;
        }

        var current = (_searchResultsList.SelectedItem as System.Windows.Controls.ListBoxItem)?.Tag;
        foreach (var (entry, tile) in _tilesByEntry)
        {
            var isMatch = _searchMatches.Contains(entry);
            tile.Opacity = isMatch ? 1.0 : 0.22;
            SetTileBackground(tile, ReferenceEquals(entry, current)
                ? ThemeBrushes.CreateBrush(_theme.HighlightColor, 1.0)
                : isMatch
                    ? ThemeBrushes.CreateBrush(_theme.HighlightColor, 0.5)
                    : Brushes.Transparent);
        }

        if (current is not null)
        {
            ScrollEntryIntoView(current);
        }
    }

    private void UpdateSearchMatches(string query)
    {
        ClearAllHighlights();
        _searchMatches.Clear();
        _searchResultsList.Items.Clear();

        if (string.IsNullOrEmpty(query))
        {
            _searchCountText.Text = string.Empty;
            _searchPopup.IsOpen = false;
            RefreshSelectionVisuals();
            RefreshCutVisuals();
            return;
        }

        foreach (var entry in GroupEntries.Enumerate(_category))
        {
            var name = GroupEntries.NameOf(entry);
            var matchIndex = name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
            if (matchIndex < 0)
            {
                continue;
            }

            _searchMatches.Add(entry);

            if (_captionsByEntry.TryGetValue(entry, out var caption))
            {
                ApplyNameHighlight(caption, name, matchIndex, query.Length);
            }

            var resultText = new TextBlock
            {
                Foreground = ThemeBrushes.CreateBrush(_theme.TextColor, 1.0),
                FontFamily = new FontFamily(_theme.ItemFontFamily),
                FontSize = _theme.ItemFontSize
            };
            ApplyNameHighlight(resultText, name, matchIndex, query.Length);

            var listItem = new System.Windows.Controls.ListBoxItem { Content = resultText, Tag = entry };
            listItem.PreviewMouseLeftButtonDown += (_, _) =>
            {
                _searchResultsList.SelectedItem = listItem;
                ActivateSelectedSearchResult();
            };
            _searchResultsList.Items.Add(listItem);
        }

        _searchCountText.Text = LocalizationService.Format("group.searchCount", _searchMatches.Count);

        if (_searchResultsList.Items.Count > 0)
        {
            _searchResultsList.SelectedIndex = 0;
        }

        _searchPopup.IsOpen = _searchResultsList.Items.Count > 0;
        ReapplySearchEmphasis();
    }

    private void MoveSearchSelection(int delta)
    {
        if (_searchResultsList.Items.Count == 0)
        {
            return;
        }

        var next = Math.Clamp(_searchResultsList.SelectedIndex + delta, 0, _searchResultsList.Items.Count - 1);
        _searchResultsList.SelectedIndex = next;
        _searchResultsList.ScrollIntoView(_searchResultsList.SelectedItem);
        ReapplySearchEmphasis();
    }

    /// <summary>Enter on a result does exactly what double-clicking the icon itself would.</summary>
    private void ActivateSelectedSearchResult()
    {
        if (_searchResultsList.SelectedItem is not System.Windows.Controls.ListBoxItem { Tag: { } entry })
        {
            return;
        }

        CloseFindOverlay(rememberQuery: true);
        if (_openActionsByEntry.TryGetValue(entry, out var open))
        {
            open();
        }
    }

    private void ApplyNameHighlight(TextBlock textBlock, string name, int matchIndex, int matchLength)
    {
        textBlock.Inlines.Clear();
        if (matchIndex < 0)
        {
            textBlock.Inlines.Add(new Run(name));
            return;
        }

        if (matchIndex > 0)
        {
            textBlock.Inlines.Add(new Run(name[..matchIndex]));
        }

        textBlock.Inlines.Add(new Run(name.Substring(matchIndex, matchLength))
        {
            FontWeight = FontWeights.Bold,
            Background = ThemeBrushes.CreateBrush(_theme.HighlightColor, 1.0)
        });

        var tailStart = matchIndex + matchLength;
        if (tailStart < name.Length)
        {
            textBlock.Inlines.Add(new Run(name[tailStart..]));
        }
    }

    private void ClearAllHighlights()
    {
        foreach (var (entry, textBlock) in _captionsByEntry)
        {
            textBlock.Text = GroupEntries.NameOf(entry);
        }
    }

    /// <summary>A flat square target in the title bar that does not start a window drag.</summary>
    private Border BuildHeaderButton(Action onClick)
    {
        var button = new Border
        {
            Width = _theme.TitleFontSize + 14,
            Height = _theme.TitleFontSize + 14,
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0)
        };

        var hover = ThemeBrushes.CreateBrush(_theme.TextColor, 0.14);
        button.MouseEnter += (_, _) => button.Background = hover;
        button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;
        button.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            onClick();
        };

        return button;
    }

    /// <summary>
    /// Hands the icon over to Explorer's own menu, third-party entries included. It is
    /// opened from the dispatcher so our themed menu has closed first: the shell menu
    /// runs its own message loop and needs the foreground to itself.
    /// </summary>
    private void ShowStandardMenu(LaunchItem item)
    {
        if (!ShellCommands.TryResolveTarget(item, out var path))
        {
            return;
        }

        var point = System.Windows.Forms.Cursor.Position;
        var extendedVerbs = Keyboard.Modifiers == ModifierKeys.Shift;

        Dispatcher.BeginInvoke(
            () => ShellContextMenu.TryShow(this, path, point, extendedVerbs),
            DispatcherPriority.ApplicationIdle);
    }

    private void OpenHeaderMenu()
    {
        // Rebuilt per click so checkmarks and enabled states reflect the current group.
        var menu = BuildHeaderContextMenu();
        menu.PlacementTarget = _menuButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>The configured shadow, or null when it is switched off.</summary>
    internal static DropShadowEffect? BuildGroupShadow(MenuTheme theme)
    {
        if (!theme.ShowShadow)
        {
            return null;
        }

        return new DropShadowEffect
        {
            BlurRadius = theme.ShadowBlurRadius,
            ShadowDepth = theme.ShadowDepth,
            Direction = theme.ShadowDirection,
            Opacity = theme.ShadowOpacity,
            Color = Colors.Black
        };
    }

    private void ApplyBackground()
    {
        UpdateHeaderTooltip();
        _border.BorderBrush = new SolidColorBrush(ComputeGroupBorderColor());

        if (_category.AreaOpacity <= 0)
        {
            _border.Background = Brushes.Transparent;
            return;
        }

        if (!string.IsNullOrWhiteSpace(_category.DesktopBackgroundImagePath) && File.Exists(_category.DesktopBackgroundImagePath))
        {
            _border.Background = new ImageBrush(new BitmapImage(new Uri(_category.DesktopBackgroundImagePath)))
            {
                Stretch = Stretch.UniformToFill,
                Opacity = _category.AreaOpacity
            };
            return;
        }

        // The group slider rides on top of the theme's own opacity rather than replacing it.
        _border.Background = ThemeBrushes.CreateBrush(_theme.BackgroundColor, _theme.Opacity * _category.AreaOpacity);
    }

    /// <summary>
    /// Derived from the face colour rather than the theme's own border swatch, so the
    /// outline always reads against whatever the group is painted with: 10% lighter than
    /// the face, except when the face is white (or close enough that lightening it would
    /// go nowhere), where 10% darker is what actually shows.
    /// </summary>
    private Color ComputeGroupBorderColor()
    {
        var face = (Color)ColorConverter.ConvertFromString(_theme.BackgroundColor)!;
        return RelativeLuminance(face) > 0.9 ? ShiftColor(face, -0.10) : ShiftColor(face, 0.10);
    }

    /// <summary>A positive factor moves each channel toward white, a negative one toward black.</summary>
    private static Color ShiftColor(Color color, double factor)
    {
        byte Shift(byte channel) => factor >= 0
            ? (byte)Math.Clamp(channel + ((255 - channel) * factor), 0, 255)
            : (byte)Math.Clamp(channel * (1 + factor), 0, 255);

        return Color.FromArgb(color.A, Shift(color.R), Shift(color.G), Shift(color.B));
    }

    private void ApplyHeaderBackground()
    {
        _header.Background = ThemeBrushes.CreateBrush(_theme.HighlightColor, _category.TitleOpacity);
    }

    /// <summary>
    /// A hover tooltip on the caption summarising the group's own settings — style,
    /// arrangement, icon size, opacity — so they are visible without opening the menu.
    /// Recomputed rather than tracked, since it is cheap and there is no single choke
    /// point every setting change already passes through.
    /// </summary>
    private void UpdateHeaderTooltip()
    {
        var lines = new List<string>
        {
            LocalizationService.Get(IsAppFolder ? "group.styleAppFolder" : "group.stylePanel")
        };

        if (_category.IconArrangement != IconArrangement.None)
        {
            lines.Add($"{LocalizationService.Get("group.arrangeIcons")} — {LocalizationService.Get("group.sortByName")}");
        }

        var iconSizeKey = _category.DesktopIconScale switch
        {
            <= 0.8 => "group.iconSizeSmall",
            >= 1.4 => "group.iconSizeLarge",
            _ => "group.iconSizeMedium"
        };
        lines.Add($"{LocalizationService.Get("group.iconSize")}: {LocalizationService.Get(iconSizeKey)}");

        lines.Add($"{LocalizationService.Get("group.areaOpacity")}: {_category.AreaOpacity * 100:0}%");

        _headerText.ToolTip = null;
    }

    private void EnsureThemeOverride()
    {
        if (_category.ThemeOverride is null)
        {
            _category.ThemeOverride = _theme.Clone();
            _theme = _category.ThemeOverride;
        }
    }

    private static string ComputeContrastingTextColor(Color background)
    {
        return RelativeLuminance(background) > 0.55 ? "#000000" : "#FFFFFF";
    }

    private static double RelativeLuminance(Color color)
    {
        return ((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) / 255.0;
    }

    /// <summary>
    /// Label colour is derived, never taken from the theme: the text sits on whatever
    /// the group is painted with, and a fixed colour disappears as soon as that changes.
    /// A background image can be anything, so those labels get white text and lean on
    /// the shadow underneath them instead.
    /// </summary>
    private Brush TileTextBrush()
    {
        if (!string.IsNullOrWhiteSpace(_category.DesktopBackgroundImagePath))
        {
            return Brushes.White;
        }

        var background = (Color)ColorConverter.ConvertFromString(_theme.BackgroundColor)!;

        // A see-through group shows the wallpaper, which is usually the darker bet.
        var effective = _theme.Opacity * _category.AreaOpacity;
        if (effective < 0.5)
        {
            return Brushes.White;
        }

        return ThemeBrushes.CreateBrush(ComputeContrastingTextColor(background), 1.0);
    }

    /// <summary>Keeps a label readable over a background image or a translucent group.</summary>
    private static DropShadowEffect TileTextShadow()
    {
        return new DropShadowEffect
        {
            BlurRadius = 4,
            ShadowDepth = 1,
            Direction = 315,
            Opacity = 0.85,
            Color = Colors.Black
        };
    }

    private ContextMenu CreateContextMenuShell()
    {
        var menu = new ContextMenu
        {
            Style = (Style)Application.Current.Resources["SmartDockGroupsContextMenuStyle"],
            Background = ThemeBrushes.CreateBrush(_theme.BackgroundColor, 1.0),
            BorderBrush = ThemeBrushes.CreateBrush(_theme.BorderColor, 1.0),
            BorderThickness = new Thickness(1)
        };
        MenuThemeProperties.SetPanelCornerRadius(menu, new CornerRadius(_theme.CornerRadius));
        return menu;
    }

    private ContextMenu BuildHeaderContextMenu()
    {
        var menu = CreateContextMenuShell();
        PopulateGroupMenu(menu);
        return menu;
    }

    /// <summary>The group's own menu, for the App Folder sheet's empty background.</summary>
    internal ContextMenu BuildGroupMenu() => BuildHeaderContextMenu();

    /// <summary>
    /// Everything that acts on the group, arranged by what it touches rather than in one
    /// long list: the group itself, what goes in it, how the icons are laid out
    /// ("Exibição"), how it is painted ("Aparência"), where it sits among the others, and
    /// the app-wide commands. The same builder feeds the header, the empty canvas, the
    /// closed App Folder tile, the App Folder sheet and the "Este grupo" submenu of an
    /// icon, so no place shows a shorter menu than another.
    /// </summary>
    private void PopulateGroupMenu(ItemsControl menu)
    {
        // ── What goes in it
        var newMenu = CreateMenuItem(LocalizationService.Get("group.newMenu"), "IconAdd");
        AddMenuItem(newMenu, LocalizationService.Get("group.newMenuShortcut"), CreateShortcut, "IconAdd");
        if (_commands is not null)
        {
            AddMenuItem(newMenu, LocalizationService.Get("group.newMenuGroup"), _commands.CreateGroup, "IconAdd");
            newMenu.Items.Add(BuildGroupLinkMenu());
        }

        menu.Items.Add(newMenu);
        AddMenuItem(menu, LocalizationService.Get("group.importShortcuts"), ImportShortcuts, "IconFolderOpen");

        var canPaste = System.Windows.Clipboard.ContainsFileDropList() || _pendingCuts.Count > 0;
        var pasteItem = CreateMenuItem(LocalizationService.Get("group.paste") + "\tCtrl+V", "IconPaste");
        pasteItem.IsEnabled = canPaste;
        pasteItem.Click += (_, _) => PasteFromClipboard();
        menu.Items.Add(pasteItem);

        // ── How it looks
        menu.Items.Add(BuildSeparator());
        menu.Items.Add(BuildViewMenu());
        menu.Items.Add(BuildAppearanceMenu());

        // ── Where it sits among the others
        var orderMenu = CreateMenuItem(LocalizationService.Get("group.windowOrder"), "IconMove");
        AddMenuItem(orderMenu, LocalizationService.Get("group.bringAllToFront"), BringAllGroupsToFront);
        AddMenuItem(orderMenu, LocalizationService.Get("group.sendOthersToBack"), SendOthersToBack);
        AddMenuItem(orderMenu, LocalizationService.Get("group.sendAllToBack"), SendAllToBack);
        orderMenu.Items.Add(BuildSeparator());
        AddMenuItem(orderMenu, LocalizationService.Get("group.bringAllToThisMonitorCentered"), BringAllGroupsToThisMonitorCentered, "IconGather");
        PopulateSendAllToMonitors(orderMenu);
        menu.Items.Add(orderMenu);

        if (_commands is not null)
        {
            AddMenuItem(menu, LocalizationService.Get("group.taskbarShortcut"), () => _commands.CreateTaskbarShortcut(_category), "IconStylePanel");
        }

        // ── This group: one section for everything that acts on the group itself
        menu.Items.Add(BuildSeparator());
        AddMenuItem(menu, LocalizationService.Get("group.rename"), OnRenameClick, "IconRename");
        if (IsAppFolder)
        {
            AddMenuItem(menu, LocalizationService.Get("group.openFolder"), OpenOverlay, "IconOpenExternal");
        }
        else
        {
            AddMenuItem(
                menu,
                _category.IsCollapsed ? LocalizationService.Get("group.expand") : LocalizationService.Get("group.collapse"),
                ToggleCollapse,
                _category.IsCollapsed ? "IconChevronDown" : "IconChevronUp");
        }

        if (_commands is not null)
        {
            AddMenuItem(
                menu,
                LocalizationService.Get(_commands.IsDocked ? "group.undockAll" : "group.dockAll"),
                () => _commands.ToggleDock(_category),
                "IconStylePanel");
            AddMenuItem(menu, LocalizationService.Get("group.duplicate"), () => _commands.Duplicate(_category), "IconDuplicate");
        }

        AddMenuItem(menu, LocalizationService.Get("group.close"), CloseGroup, "IconClose");
        AddMenuItem(menu, LocalizationService.Get("group.remove"), OnDeleteGroupClick, "IconDelete");

        // ── The app: always last
        if (_commands is not null)
        {
            menu.Items.Add(BuildSeparator());
            AddMenuItem(menu, LocalizationService.Get("tray.settings"), _commands.OpenSettings, "IconSettings");
        }
    }

    private MenuItem BuildViewMenu()
    {
        var viewMenu = CreateMenuItem(LocalizationService.Get("group.viewMenu"), "IconGrid");
        // Docked, the group is always a title bar in the stack, so this flips the style it will have
        // again on undock (shown here as the style it currently "has"), instead of being greyed out.
        var docked = _isDockedMember && _commands is not null;
        var asFolder = docked
            ? _commands!.DockedStyle(_category) == DesktopGroupDisplayMode.AppFolder
            : IsAppFolder;
        var styleItem = CreateMenuItem(
            LocalizationService.Get(asFolder ? "group.stylePanel" : "group.styleAppFolder"),
            asFolder ? "IconStylePanel" : "IconStyleAppFolder");
        styleItem.Click += (_, _) =>
        {
            if (docked)
            {
                _commands!.DockToggleStyle(_category);
            }
            else
            {
                ToggleDisplayMode();
            }
        };
        viewMenu.Items.Add(styleItem);
        AddCheckItem(viewMenu, LocalizationService.Get("group.arrangeIcons"), _category.IconArrangement != IconArrangement.None, ToggleArrangeIconsAutomatically, "IconSort");

        var sizeMenu = CreateMenuItem(LocalizationService.Get("group.iconSize"), "IconIconSize");
        AddCheckItem(sizeMenu, LocalizationService.Get("group.iconSizeSmall"), IsIconScale(0.75), () => SetIconScale(0.75));
        AddCheckItem(sizeMenu, LocalizationService.Get("group.iconSizeMedium"), IsIconScale(1.0), () => SetIconScale(1.0));
        AddCheckItem(sizeMenu, LocalizationService.Get("group.iconSizeLarge"), IsIconScale(1.5), () => SetIconScale(1.5));
        viewMenu.Items.Add(sizeMenu);

        var spacingMenu = CreateMenuItem(LocalizationService.Get("group.iconSpacing"), "IconSpacing");
        AddSpacingSlider(spacingMenu, LocalizationService.Get("group.iconHGap"), _category.IconHGap, SetIconHGap);
        AddSpacingSlider(spacingMenu, LocalizationService.Get("group.iconVGap"), _category.IconVGap, SetIconVGap);
        viewMenu.Items.Add(spacingMenu);
        return viewMenu;
    }

    private MenuItem BuildAppearanceMenu()
    {
        var lookMenu = CreateMenuItem(LocalizationService.Get("group.shareVisual"), "IconColor");
        AddMenuItem(lookMenu, LocalizationService.Get("group.backgroundColor"), OnChangeColorClick, "IconColor");
        AddMenuItem(lookMenu, LocalizationService.Get("group.backgroundImage"), OnChangeBackgroundImageClick, "IconImage");
        if (_commands is not null)
        {
            AddMenuItem(lookMenu, LocalizationService.Get("group.wallpaperAsBackground"), () => _commands.ApplyWallpaper(_category), "IconImage");
        }

        var removeImage = CreateMenuItem(LocalizationService.Get("group.removeBackgroundImage"));
        removeImage.IsEnabled = !string.IsNullOrWhiteSpace(_category.DesktopBackgroundImagePath);
        removeImage.Click += (_, _) => OnClearBackgroundImageClick();
        lookMenu.Items.Add(removeImage);

        var opacityMenu = CreateMenuItem(LocalizationService.Get("group.opacity"), "IconOpacity");
        AddOpacitySlider(opacityMenu, LocalizationService.Get("group.areaOpacity"), _category.AreaOpacity, SetAreaOpacity);
        AddOpacitySlider(opacityMenu, LocalizationService.Get("group.titleOpacity"), _category.TitleOpacity, SetTitleOpacity);
        lookMenu.Items.Add(opacityMenu);

        if (_commands is not null)
        {
            lookMenu.Items.Add(BuildSeparator());
            lookMenu.Items.Add(BuildShareMenu());
        }

        return lookMenu;
    }

    /// <summary>
    /// Sharing one aspect at a time: the image without forcing the colour, the colour
    /// without the image, the spacing alone… either onto every other group right now, or
    /// as the starting point for groups created later.
    /// </summary>
    private MenuItem BuildShareMenu()
    {
        var share = CreateMenuItem(LocalizationService.Get("group.shareWithOthers"), "IconDuplicate");

        MenuItem Aspects(string headerKey, bool asDefault)
        {
            var parent = CreateMenuItem(LocalizationService.Get(headerKey));
            AddMenuItem(parent, LocalizationService.Get("group.aspectAll"), () => _commands!.ShareVisual(_category, VisualAspects.All, asDefault));
            parent.Items.Add(BuildSeparator());
            AddMenuItem(parent, LocalizationService.Get("group.aspectColor"), () => _commands!.ShareVisual(_category, VisualAspects.BackgroundColor, asDefault), "IconColor");
            AddMenuItem(parent, LocalizationService.Get("group.aspectImage"), () => _commands!.ShareVisual(_category, VisualAspects.BackgroundImage, asDefault), "IconImage");
            AddMenuItem(parent, LocalizationService.Get("group.aspectOpacity"), () => _commands!.ShareVisual(_category, VisualAspects.Opacity, asDefault), "IconOpacity");
            AddMenuItem(parent, LocalizationService.Get("group.aspectSpacing"), () => _commands!.ShareVisual(_category, VisualAspects.IconSpacing, asDefault), "IconSpacing");
            AddMenuItem(parent, LocalizationService.Get("group.aspectIconSize"), () => _commands!.ShareVisual(_category, VisualAspects.IconSize, asDefault), "IconIconSize");
            return parent;
        }

        share.Items.Add(Aspects("group.applyVisualToAll", asDefault: false));
        share.Items.Add(Aspects("group.setAsDefaultVisual", asDefault: true));
        share.Items.Add(BuildSeparator());
        AddMenuItem(share, LocalizationService.Get("group.wallpaperForAll"), () => _commands!.ApplyWallpaper(null), "IconImage");
        return share;
    }

    private ContextMenu BuildItemTileContextMenu(LaunchItem item)
    {
        return BuildEntryMenu(item, _category, _selectedEntries, () => _onExecute(item), this);
    }

    private ContextMenu BuildFolderTileContextMenu(MenuCategory folder)
    {
        return BuildEntryMenu(folder, _category, _selectedEntries, () => OpenSubfolder(folder), this);
    }

    /// <summary>
    /// The menu of one icon, the same in the panel and in the App Folder sheet: open it,
    /// move it through the clipboard, change it, then the Windows commands for the file,
    /// and at the bottom the group's own menu and "new group" — reachable without first
    /// finding an empty spot to right-click. <paramref name="selection"/> is whatever is
    /// highlighted where the click happened; commands act on all of it when the clicked
    /// icon is part of it, the way Explorer does.
    /// </summary>
    internal ContextMenu BuildEntryMenu(object entry, MenuCategory container, IReadOnlyCollection<object> selection, Action open, Window dialogOwner)
    {
        var menu = CreateContextMenuShell();
        var targets = selection.Count > 1 && selection.Contains(entry) ? selection.ToList() : new List<object> { entry };
        var item = entry as LaunchItem;
        var hasFile = item is not null && ShellCommands.HasFileTarget(item);

        // ── Open
        AddMenuItem(menu, LocalizationService.Get("item.open"), open, entry is MenuCategory ? "IconFolder" : "IconOpenExternal");
        if (hasFile)
        {
            AddMenuItem(menu, LocalizationService.Get("item.runAsAdmin"), () => ShellCommands.RunAsAdministrator(item!), "IconShield");
            AddMenuItem(menu, LocalizationService.Get("item.openFileLocation"), () => ShellCommands.RevealInExplorer(item!), "IconFolderOpen");
        }

        // ── Clipboard
        menu.Items.Add(BuildSeparator());
        AddMenuItem(menu, LocalizationService.Get("item.cut") + "\tCtrl+X", () => CopyEntriesToClipboard(targets, cut: true), "IconCut");
        AddMenuItem(menu, LocalizationService.Get("item.copy") + "\tCtrl+C", () => CopyEntriesToClipboard(targets, cut: false), "IconCopy");
        if (hasFile)
        {
            AddMenuItem(menu, LocalizationService.Get("item.copyPath"), () => ShellCommands.CopyPath(item!), "IconCopy");
        }

        // ── Change it
        menu.Items.Add(BuildSeparator());
        AddMenuItem(menu, LocalizationService.Get("item.rename") + "\tF2", () => RenameEntry(container, entry, dialogOwner), "IconRename");
        if (item is not null && IsTargetMissing(item))
        {
            AddMenuItem(menu, LocalizationService.Get("item.locateTarget"), () => LocateMissingTarget(item), "IconSearch");
        }

        AddMenuItem(menu, LocalizationService.Get("item.removeFromGroup") + "\tDel", () => RemoveEntries(container, targets, dialogOwner), "IconDelete");

        // ── Windows
        if (hasFile)
        {
            menu.Items.Add(BuildSeparator());
            AddMenuItem(menu, LocalizationService.Get("item.properties"), () => ShellCommands.ShowProperties(item!), "IconProperties");
            AddMenuItem(menu, LocalizationService.Get("item.standardMenu"), () => ShowStandardMenu(item!), "IconShellMenu");
        }

        // ── The group and new groups
        menu.Items.Add(BuildSeparator());
        var groupMenu = CreateMenuItem(LocalizationService.Format("item.thisGroup", _category.Name), "IconMore");
        PopulateGroupMenu(groupMenu);
        menu.Items.Add(groupMenu);
        if (_commands is not null)
        {
            AddMenuItem(menu, LocalizationService.Get("group.newGroup"), _commands.CreateGroup, "IconAdd");
        }

        return menu;
    }

    private MenuItem CreateMenuItem(string header, string? iconKey = null)
    {
        var item = new MenuItem
        {
            Header = header,
            Style = (Style)Application.Current.Resources["SmartDockGroupsMenuItemStyle"],
            Foreground = ThemeBrushes.CreateBrush(_theme.TextColor, 1.0),
            FontFamily = new FontFamily(_theme.ItemFontFamily),
            FontSize = _theme.ItemFontSize,
            Padding = new Thickness(_theme.ItemPadding, _theme.ItemPadding / 2, _theme.ItemPadding, _theme.ItemPadding / 2),
            Icon = BuildMenuIcon(iconKey),

            // Read by the submenu popup in the template, not by the row itself.
            Background = ThemeBrushes.CreateBrush(_theme.BackgroundColor, _theme.Opacity),
            BorderBrush = ThemeBrushes.CreateBrush(_theme.BorderColor, 1.0)
        };
        MenuThemeProperties.SetHighlightBrush(item, ThemeBrushes.CreateBrush(_theme.HighlightColor, 1.0));
        MenuThemeProperties.SetHighlightCornerRadius(item, new CornerRadius(4));
        MenuThemeProperties.SetPanelCornerRadius(item, new CornerRadius(_theme.CornerRadius));
        return item;
    }

    /// <summary>
    /// Every row reserves the icon column even when it has no icon, so headers stay on
    /// one vertical line instead of stepping in and out as icons come and go.
    /// </summary>
    private FrameworkElement BuildMenuIcon(string? iconKey)
    {
        var size = _theme.ItemFontSize + 3;
        if (iconKey is null)
        {
            return new Border { Width = size, Height = size };
        }

        return AppIcons.Create(iconKey, ThemeBrushes.CreateBrush(_theme.TextColor, 0.85), size)
            ?? new Border { Width = size, Height = size };
    }

    private Separator BuildSeparator()
    {
        return new Separator
        {
            Background = ThemeBrushes.CreateBrush(_theme.BorderColor, 1.0),
            Height = 1,
            Margin = new Thickness(6, 4, 6, 4)
        };
    }

    private bool IsIconScale(double scale) => Math.Abs(_category.DesktopIconScale - scale) < 0.01;

    private void AddCheckItem(ItemsControl parent, string header, bool isChecked, Action handler, string? iconKey = null)
    {
        // A tick in the icon column is how Windows shows a setting that is on; when it
        // is off the row keeps its own icon, or an empty column if it has none.
        AddMenuItem(parent, header, handler, isChecked ? "IconCheck" : iconKey);
    }

    /// <summary>
    /// A live slider inside the menu. The value is applied while dragging so the group
    /// updates under the pointer, but only written to the config once the drag ends.
    /// </summary>
    private void AddOpacitySlider(ItemsControl parent, string label, double value, Action<double> apply)
    {
        var caption = new TextBlock
        {
            Foreground = ThemeBrushes.CreateBrush(_theme.TextColor, 1.0),
            FontFamily = new FontFamily(_theme.ItemFontFamily),
            FontSize = _theme.ItemFontSize - 1
        };

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            Value = value,
            Width = 168,
            SmallChange = 0.05,
            LargeChange = 0.1,
            Margin = new Thickness(0, 6, 0, 0)
        };

        void UpdateCaption() => caption.Text = $"{label}   {slider.Value * 100:0}%";

        UpdateCaption();
        slider.ValueChanged += (_, _) =>
        {
            UpdateCaption();
            apply(slider.Value);
        };
        slider.LostMouseCapture += (_, _) => _onLayoutChanged(_category);
        slider.KeyUp += (_, _) => _onLayoutChanged(_category);

        var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 2, 0, 4) };
        panel.Children.Add(caption);
        panel.Children.Add(slider);

        var item = CreateMenuItem(string.Empty);
        item.Header = panel;
        item.StaysOpenOnClick = true;
        MenuThemeProperties.SetHighlightBrush(item, Brushes.Transparent);
        parent.Items.Add(item);
    }

    private void AddSpacingSlider(ItemsControl parent, string label, double value, Action<double> apply)
    {
        var caption = new TextBlock
        {
            Foreground = ThemeBrushes.CreateBrush(_theme.TextColor, 1.0),
            FontFamily = new FontFamily(_theme.ItemFontFamily),
            FontSize = _theme.ItemFontSize - 1
        };

        var slider = new Slider
        {
            Minimum = 10,
            Maximum = 30,
            Value = Math.Clamp(value, 10, 30),
            Width = 168,
            SmallChange = 1,
            LargeChange = 5,
            IsSnapToTickEnabled = true,
            TickFrequency = 1,
            Margin = new Thickness(0, 6, 0, 0)
        };

        void UpdateCaption() => caption.Text = $"{label}   {slider.Value:0} px";

        UpdateCaption();
        slider.ValueChanged += (_, _) =>
        {
            UpdateCaption();
            apply(slider.Value);
        };
        slider.LostMouseCapture += (_, _) => _onLayoutChanged(_category);
        slider.KeyUp += (_, _) => _onLayoutChanged(_category);

        var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 2, 0, 4) };
        panel.Children.Add(caption);
        panel.Children.Add(slider);

        var item = CreateMenuItem(string.Empty);
        item.Header = panel;
        item.StaysOpenOnClick = true;
        MenuThemeProperties.SetHighlightBrush(item, Brushes.Transparent);
        parent.Items.Add(item);
    }

    private void AddMenuItem(ItemsControl parent, string header, Action handler, string? iconKey = null)
    {
        var item = CreateMenuItem(header, iconKey);
        item.Click += (_, _) => handler();
        parent.Items.Add(item);
    }

    private void WarnLimit(int notAdded)
    {
        MessageBox.Show(
            this,
            LocalizationService.Format("group.limitReached", GroupLimits.MaxEntries, notAdded),
            LocalizationService.Get("common.appName"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void WarnLinkRefused()
    {
        MessageBox.Show(
            this,
            LocalizationService.Get("group.linkRefused"),
            LocalizationService.Get("common.appName"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    /// <summary>Adds a shortcut to another group in this one.</summary>
    private void AddGroupLink(MenuCategory target)
    {
        if (GroupLimits.Room(_category) == 0)
        {
            WarnLimit(1);
            return;
        }

        if (target.Id is null || !GroupLimits.CanHoldLinkTo(_category, target.Id))
        {
            WarnLinkRefused();
            return;
        }

        var link = GroupLimits.CreateGroupLink(_category, target);
        PlaceInFreeCells([link]);
        _category.Items.Add(link);
        FinishStructuralChange();
        SelectEntries([link]);
    }

    /// <summary>"Shortcut to a group ▸": every other group, the ones already linked here greyed out.</summary>
    private MenuItem BuildGroupLinkMenu()
    {
        var menu = CreateMenuItem(LocalizationService.Get("group.newMenuGroupLink"), "IconStylePanel");
        var candidates = (_commands?.DesktopGroups ?? [])
            .Where(group => !ReferenceEquals(group, _category) && group.Id is not null)
            .ToList();

        foreach (var group in candidates)
        {
            var item = CreateMenuItem(group.Name, "IconStylePanel");
            item.IsEnabled = GroupLimits.CanHoldLinkTo(_category, group.Id!);
            var captured = group;
            item.Click += (_, _) => AddGroupLink(captured);
            menu.Items.Add(item);
        }

        menu.IsEnabled = candidates.Count > 0;
        return menu;
    }

    private void CreateShortcut()
    {
        if (GroupLimits.Room(_category) == 0)
        {
            WarnLimit(1);
            return;
        }

        var newItem = new LaunchItem
        {
            Name = string.Empty,
            Type = LaunchItemType.Application,
            Target = string.Empty,
            IsDesktopPinned = true
        };
        var editor = new Settings.LaunchItemEditWindow(
            newItem,
            name => GroupNames.IsTaken(_category, name) ? LocalizationService.Format("group.nameTaken", name) : null)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Topmost = true
        };
        if (editor.ShowDialog() == true)
        {
            newItem.IsDesktopPinned = true;
            newItem.Target = ShortcutStore.Adopt(newItem.Target);
            PlaceInFreeCells([newItem]);
            _category.Items.Add(newItem);
            FinishStructuralChange();
            SelectEntries([newItem]);
        }
    }

    private void CreateSubfolder()
    {
        if (GroupLimits.Room(_category) == 0)
        {
            WarnLimit(1);
            return;
        }

        var prompt = new Settings.TextPromptWindow(
            LocalizationService.Get("group.folderNamePrompt"),
            string.Empty,
            validate: name => GroupNames.IsTaken(_category, name) ? LocalizationService.Format("group.nameTaken", name) : null);
        if (prompt.ShowDialog() != true)
        {
            return;
        }

        _category.Categories.Add(new MenuCategory { Name = prompt.Value });
        FinishStructuralChange();
    }

    private void CreateTextFile()
    {
        if (GroupLimits.Room(_category) == 0)
        {
            WarnLimit(1);
            return;
        }

        var directory = GetGroupFilesDirectory();
        Directory.CreateDirectory(directory);
        var fileName = GetAvailableFileName(directory, LocalizationService.Get("group.newTextFileName"), ".txt");
        var fullPath = Path.Combine(directory, fileName);
        File.WriteAllText(fullPath, string.Empty);

        var item = new LaunchItem
        {
            Name = GroupNames.MakeUnique(_category, Path.GetFileNameWithoutExtension(fileName)),
            Type = LaunchItemType.File,
            Target = fullPath,
            IsDesktopPinned = true
        };

        _category.Items.Add(item);
        FinishStructuralChange();
    }

    private string GetGroupFilesDirectory()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SmartDockGroups", "GroupFiles");
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeName = string.Concat(_category.Name.Select(c => invalidChars.Contains(c) ? '_' : c));
        return Path.Combine(root, safeName);
    }

    private static string GetAvailableFileName(string directory, string baseName, string extension)
    {
        var candidate = baseName + extension;
        var counter = 2;
        while (File.Exists(Path.Combine(directory, candidate)))
        {
            candidate = $"{baseName} ({counter}){extension}";
            counter++;
        }

        return candidate;
    }

    /// <summary>
    /// Renames an icon, refusing — with the reason, and the dialog left open — a name
    /// another entry of the same group already uses.
    /// </summary>
    internal void RenameEntry(MenuCategory container, object entry, Window owner)
    {
        var prompt = new Settings.TextPromptWindow(
            LocalizationService.Get(entry is MenuCategory ? "group.folderNamePrompt" : "item.renamePrompt"),
            GroupEntries.NameOf(entry),
            validate: name => GroupNames.IsTaken(container, name, entry)
                ? LocalizationService.Format("group.nameTaken", name)
                : null)
        {
            Owner = owner.IsVisible ? owner : null
        };

        if (prompt.ShowDialog() != true)
        {
            return;
        }

        switch (entry)
        {
            case LaunchItem item:
                item.Name = prompt.Value;
                break;
            case MenuCategory folder:
                folder.Name = prompt.Value;
                break;
        }

        FinishStructuralChange();
    }

    /// <summary>
    /// Removes icons from <paramref name="container"/> after one confirmation. A folder
    /// that still holds shortcuts can be removed too — the question then says what will be
    /// lost, and "No" is the default answer.
    /// </summary>
    internal void RemoveEntries(MenuCategory container, IReadOnlyCollection<object> entries, Window owner)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var names = string.Join(", ", entries.Select(GroupEntries.NameOf));
        var lost = entries.OfType<MenuCategory>().Sum(CountShortcuts);

        var confirmed = lost > 0
            ? MessageBox.Show(
                owner,
                LocalizationService.Format("item.removeWithContentsConfirm", names, lost),
                LocalizationService.Get("common.appName"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No)
            : MessageBox.Show(
                owner,
                LocalizationService.Format("item.removeConfirm", names),
                LocalizationService.Get("common.appName"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        foreach (var entry in entries)
        {
            switch (entry)
            {
                case LaunchItem item:
                    container.Items.Remove(item);
                    break;
                case MenuCategory folder:
                    container.Categories.Remove(folder);
                    if (_openSubfolders.TryGetValue(folder, out var openWindow))
                    {
                        openWindow.Close();
                        _openSubfolders.Remove(folder);
                    }

                    break;
            }
        }

        FinishStructuralChange();
    }

    /// <summary>Every shortcut in a group, including those inside its subfolders.</summary>
    private static int CountShortcuts(MenuCategory group)
    {
        return group.Items.Count + group.Categories.Sum(CountShortcuts);
    }

    private void DeleteFolder(MenuCategory folder)
    {
        if (_openSubfolders.TryGetValue(folder, out var openWindow))
        {
            openWindow.Close();
            _openSubfolders.Remove(folder);
        }

        _category.Categories.Remove(folder);
        FinishStructuralChange();
    }

    private void OpenSubfolder(MenuCategory folder)
    {
        if (_openSubfolders.TryGetValue(folder, out var existing))
        {
            existing.Activate();
            return;
        }

        var theme = folder.ThemeOverride ?? _theme;
        var window = new DesktopGroupWindow(
            folder,
            theme,
            _iconCache,
            _onExecute,
            _ => _onLayoutChanged(_category),
            DeleteFolder);

        window.Closed += (_, _) => _openSubfolders.Remove(folder);
        _openSubfolders[folder] = window;
        window.Show();
    }

    private void OnRenameClick()
    {
        var prompt = new Settings.TextPromptWindow(LocalizationService.Get("group.namePrompt"), _category.Name);
        if (prompt.ShowDialog() != true)
        {
            return;
        }

        _category.Name = prompt.Value;
        Title = prompt.Value;
        UpdateHeaderTitle();
        RefreshFolderTile();
        _onLayoutChanged(_category);
    }

    private void OnChangeColorClick()
    {
        using var dialog = new System.Windows.Forms.ColorDialog();
        var current = (Color)ColorConverter.ConvertFromString(_theme.BackgroundColor)!;
        dialog.Color = System.Drawing.Color.FromArgb(current.A, current.R, current.G, current.B);

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        EnsureThemeOverride();
        var newColor = Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
        _category.ThemeOverride!.BackgroundColor = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        _category.ThemeOverride.TextColor = ComputeContrastingTextColor(newColor);
        ApplyThemeChange();
    }

    private void SetAreaOpacity(double value)
    {
        _category.AreaOpacity = value;
        ApplyBackground();
        _onLayoutChanged(_category);
    }

    private void SetTitleOpacity(double value)
    {
        _category.TitleOpacity = value;
        ApplyHeaderBackground();
        _onLayoutChanged(_category);
    }

    private void SetIconHGap(double value)
    {
        _category.IconHGap = (int)Math.Clamp(Math.Round(value), 10, 30);
        FinishStructuralChange();
    }

    private void SetIconVGap(double value)
    {
        _category.IconVGap = (int)Math.Clamp(Math.Round(value), 10, 30);
        FinishStructuralChange();
    }

    private void ToggleCollapse()
    {
        if (_isDockedMember && _commands is not null)
        {
            // In the stack only one group is open: the stack decides who closes.
            _commands.DockToggleExpanded(_category);
            return;
        }

        SetCollapsed(!_category.IsCollapsed);

        // Expanding near the bottom or right edge must not push the group off the screen.
        if (!_category.IsCollapsed)
        {
            KeepOnScreen(byCursor: false);
            _category.DesktopX = Left;
            _category.DesktopY = Top;
        }

        _onLayoutChanged(_category);
    }

    private void SetCollapsed(bool collapsed)
    {
        _category.IsCollapsed = collapsed;
        _scroller.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        SetResizeHandlesVisible(!collapsed);

        UpdateCollapseGlyph();
        ApplyDisplayMode();
    }

    private void UpdateCollapseGlyph()
    {
        _collapseGlyph.Child = AppIcons.Create(
            _category.IsCollapsed ? "IconChevronDown" : "IconChevronUp",
            ThemeBrushes.CreateBrush(_theme.TextColor, 1.0),
            _theme.TitleFontSize + 3);
        _collapseGlyph.ToolTip = LocalizationService.Get(_category.IsCollapsed ? "group.expand" : "group.collapse");
    }

    private void OnChangeBackgroundImageClick()
    {
        var dialog = new OpenFileDialog
        {
            Filter = LocalizationService.Get("group.imageFilter")
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _category.DesktopBackgroundImagePath = dialog.FileName;
        ApplyBackground();
        _onLayoutChanged(_category);
    }

    private void OnClearBackgroundImageClick()
    {
        _category.DesktopBackgroundImagePath = null;
        ApplyBackground();
        _onLayoutChanged(_category);
    }

    /// <summary>
    /// Any group can be removed, not only an empty one. When it still holds shortcuts the
    /// question says how many will be lost, and "No" is the default answer, so a stray
    /// Enter keeps the group.
    /// </summary>
    private void OnDeleteGroupClick()
    {
        var shortcuts = CountShortcuts(_category);
        var confirmed = shortcuts > 0
            ? MessageBox.Show(
                this,
                LocalizationService.Format("group.deleteWithContentsConfirm", _category.Name, shortcuts),
                LocalizationService.Get("common.appName"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No)
            : MessageBox.Show(
                this,
                LocalizationService.Format("group.deleteConfirm", _category.Name),
                LocalizationService.Get("common.appName"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

        if (confirmed == MessageBoxResult.Yes)
        {
            _onDeleteRequested(_category);
        }
    }

    /// <summary>Rebuilds the whole window after its category was rewritten from outside.</summary>
    public void ReloadVisuals(MenuTheme theme)
    {
        _theme = theme;
        Content = BuildRoot();
        SetCollapsed(_category.IsCollapsed);
    }

    private void ApplyThemeChange()
    {
        ApplyBackground();
        ApplyHeaderBackground();
        _headerText.Foreground = ThemeBrushes.CreateBrush(_theme.TextColor, 1.0);
        UpdateCollapseGlyph();
        PopulateTiles();
        ApplyDisplayMode();
        _onLayoutChanged(_category);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            return;
        }

        var paths = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop)!;
        AddIncomingPaths(paths, e.GetPosition(_canvas));
    }

    /// <summary>
    /// The one way files from outside join this group — dropped, pasted or imported.
    /// Shortcut files are adopted (see <see cref="ShortcutStore"/>) so the group keeps
    /// working after the original is deleted; the same shortcut twice is not added again;
    /// a name already used here gets a " (2)" suffix instead of a twin. Returns what was
    /// actually added.
    /// </summary>
    internal List<LaunchItem> AddIncomingPaths(IEnumerable<string> paths, System.Windows.Point? dropPoint = null)
    {
        var added = new List<LaunchItem>();
        var offset = 0.0;
        var overLimit = 0;

        foreach (var path in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            var item = new LaunchItem
            {
                Name = Path.GetFileNameWithoutExtension(path),
                Type = InferType(path),
                Target = ShortcutStore.Adopt(path),
                IsDesktopPinned = true
            };

            // Already here under this name, pointing at this shortcut: nothing to add.
            if (GroupNames.FindSameShortcut(_category, item) is not null)
            {
                continue;
            }

            if (GroupLimits.Room(_category) == 0)
            {
                overLimit++;
                continue;
            }

            item.Name = GroupNames.MakeUnique(_category, item.Name);

            if (dropPoint is { } point)
            {
                item.DesktopIconX = point.X + offset;
                item.DesktopIconY = point.Y + offset;
                offset += 16;
            }

            _category.Items.Add(item);
            added.Add(item);
        }

        if (dropPoint is null)
        {
            PlaceInFreeCells(added);
        }

        if (overLimit > 0)
        {
            WarnLimit(overLimit);
        }

        if (added.Count > 0)
        {
            FinishStructuralChange();
            SelectEntries(added);
        }

        return added;
    }

    /// <summary>
    /// Gives each item the first grid cell no icon occupies yet, in reading order, so a
    /// paste of several shortcuts lands as a tidy row instead of a pile at the corner.
    /// </summary>
    private void PlaceInFreeCells(IReadOnlyCollection<LaunchItem> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        var usableWidth = _category.DesktopWidth - (PaddingX * 2);
        var columns = Math.Max(1, (int)(usableWidth / (HStride * _category.DesktopIconScale)));

        var occupied = new List<Rect>();
        foreach (var entry in GroupEntries.Enumerate(_category))
        {
            var (x, y) = entry switch
            {
                LaunchItem i when !items.Contains(i) => (i.DesktopIconX, i.DesktopIconY),
                MenuCategory f => (f.IconX, f.IconY),
                _ => ((double?)null, (double?)null)
            };

            if (x is not null && y is not null)
            {
                occupied.Add(new Rect(x.Value, y.Value, TileSize - 8, TileSize - 8));
            }
        }

        var cell = 0;
        foreach (var item in items)
        {
            while (true)
            {
                var x = PaddingX + ((cell % columns) * HStride);
                var y = PaddingY + ((cell / columns) * VStride);
                var candidate = new Rect(x, y, TileSize - 8, TileSize - 8);
                cell++;

                if (!occupied.Any(r => r.IntersectsWith(candidate)))
                {
                    item.DesktopIconX = x;
                    item.DesktopIconY = y;
                    occupied.Add(candidate);
                    break;
                }
            }
        }
    }

    private void SelectEntries(IEnumerable<object> entries)
    {
        _selectedEntries.Clear();
        foreach (var entry in entries)
        {
            _selectedEntries.Add(entry);
        }

        RefreshSelectionVisuals();
        if (_selectedEntries.FirstOrDefault() is { } first)
        {
            ScrollEntryIntoView(first);
        }
    }

    /// <summary>Picks several files at once — typically a folder full of shortcuts, Ctrl+A — and adds them all.</summary>
    private void ImportShortcuts()
    {
        var paths = PickShortcutFiles(this);
        if (paths.Length > 0)
        {
            AddIncomingPaths(paths);
        }
    }

    /// <summary>
    /// A multi-select file picker that returns the shortcut files themselves, not what they
    /// point at (<c>DereferenceLinks = false</c>) — the shortcut carries the arguments,
    /// working folder and icon the group should keep.
    /// </summary>
    internal static string[] PickShortcutFiles(Window? owner)
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            DereferenceLinks = false,
            Title = LocalizationService.Get("group.importShortcutsTitle"),
            Filter = LocalizationService.Get("group.importShortcutsFilter"),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };

        return dialog.ShowDialog(owner) == true ? dialog.FileNames : [];
    }

    private static LaunchItemType InferType(string path)
    {
        if (Directory.Exists(path))
        {
            return LaunchItemType.Folder;
        }

        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase)
            ? LaunchItemType.Application
            : LaunchItemType.File;
    }

    private void PopulateTiles()
    {
        _canvas.Children.Clear();
        _tilesByEntry.Clear();
        _captionsByEntry.Clear();
        _openActionsByEntry.Clear();
        _selectedEntries.Clear();
        var index = 0;
        foreach (var folder in _category.Categories)
        {
            AddFolderTile(
                folder,
                folder.IconX ?? PaddingX + ((index % 3) * HStride),
                folder.IconY ?? PaddingY + ((index / 3) * VStride));
            index++;
        }

        foreach (var item in _category.Items)
        {
            item.IsDesktopPinned = true;
            AddTile(
                item,
                item.DesktopIconX ?? PaddingX + ((index % 3) * HStride),
                item.DesktopIconY ?? PaddingY + ((index / 3) * VStride));
            index++;
        }

        RefreshFolderTile();
        RefreshCutVisuals();
        UpdateCanvasExtent();
        UpdateHeaderTitle();
        ReapplySearchEmphasis();
    }

    /// <summary>
    /// Sizes the canvas to reach the furthest icon, so the scroller offers exactly the
    /// room the icons need — and never less than the visible area, so an empty stretch
    /// of the panel still takes clicks, marquee drags and the right-click menu.
    /// </summary>
    private void UpdateCanvasExtent()
    {
        if (_scroller is null)
        {
            return;
        }

        var scale = _category.DesktopIconScale > 0 ? _category.DesktopIconScale : 1.0;
        var right = 0.0;
        var bottom = 0.0;
        foreach (var tile in _tilesByEntry.Values)
        {
            var left = Canvas.GetLeft(tile);
            var top = Canvas.GetTop(tile);
            var width = tile.ActualWidth > 0 ? tile.ActualWidth : TileSize - 8;
            var height = tile.ActualHeight > 0 ? tile.ActualHeight : TileSize;
            right = Math.Max(right, (double.IsNaN(left) ? 0 : left) + width);
            bottom = Math.Max(bottom, (double.IsNaN(top) ? 0 : top) + height);
        }

        var contentWidth = right + (PaddingX / 2);
        var contentHeight = bottom + PaddingY;

        // The room left once the other direction's scrollbar, if it will be shown, takes its
        // strip. Filling the full width before the vertical bar appeared is what used to
        // leave a few pixels of overflow — and a pointless horizontal scrollbar.
        var needsVertical = contentHeight * scale > _scroller.ActualHeight;
        var needsHorizontal = contentWidth * scale > _scroller.ActualWidth - (needsVertical ? SystemParameters.VerticalScrollBarWidth : 0);
        var roomWidth = Math.Max(0, _scroller.ActualWidth - (needsVertical ? SystemParameters.VerticalScrollBarWidth : 0));
        var roomHeight = Math.Max(0, _scroller.ActualHeight - (needsHorizontal ? SystemParameters.HorizontalScrollBarHeight : 0));

        // Visible area first; a scrollbar appears only when an icon really reaches past it.
        _canvas.Width = Math.Max(contentWidth, roomWidth / scale);
        _canvas.Height = Math.Max(contentHeight, roomHeight / scale);
    }

    /// <summary>Scrolls the panel just enough to show this entry's icon.</summary>
    private void ScrollEntryIntoView(object entry)
    {
        if (_tilesByEntry.TryGetValue(entry, out var tile))
        {
            tile.BringIntoView();
        }
    }

    private void ToggleArrangeIconsAutomatically()
    {
        if (_category.IconArrangement != IconArrangement.None)
        {
            _category.IconArrangement = IconArrangement.None;
            _onLayoutChanged(_category);
            UpdateHeaderTooltip();
        }
        else
        {
            EnableAutoArrange();
        }
    }

    internal static void BringAllGroupsToFront()
    {
        foreach (var window in _allGroupWindows.ToList())
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetWindowPos(hwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }
    }

    private void SendOthersToBack()
    {
        foreach (var window in _allGroupWindows.Where(w => !ReferenceEquals(w, this)).ToList())
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }

        var thisHwnd = new WindowInteropHelper(this).Handle;
        if (thisHwnd != IntPtr.Zero)
        {
            SetWindowPos(thisHwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    internal static void SendAllToBack()
    {
        foreach (var window in _allGroupWindows.ToList())
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }
    }

    private void PopulateSendAllToMonitors(ItemsControl orderMenu)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var areas = DisplayInventory.WorkAreas(this);

        for (var i = 0; i < screens.Length && i < areas.Count; i++)
        {
            var targetIndex = i;
            var screen = screens[i];
            var area = areas[i];
            var label = screen.Primary
                ? LocalizationService.Format("group.sendAllToMonitorPrimary", i + 1, (int)area.Width, (int)area.Height)
                : LocalizationService.Format("group.sendAllToMonitor", i + 1, (int)area.Width, (int)area.Height);

            AddMenuItem(orderMenu, label, () => SendAllGroupsToMonitor(targetIndex));
        }
    }

    private void BringAllGroupsToThisMonitorCentered()
    {
        var areas = DisplayInventory.WorkAreas(this);
        if (areas.Count == 0)
        {
            return;
        }

        if (_isDockedMember && _commands is not null)
        {
            _commands.MoveDockInto(areas[Math.Clamp(MonitorPlacement.IndexOfOwner(HomeRect, areas), 0, areas.Count - 1)]);
            return;
        }

        var currentMonitorIndex = MonitorPlacement.IndexOfOwner(HomeRect, areas);
        if (currentMonitorIndex < 0 || currentMonitorIndex >= areas.Count)
        {
            currentMonitorIndex = 0;
        }

        var destArea = areas[currentMonitorIndex];
        var windows = _allGroupWindows.ToList();
        if (windows.Count == 0)
        {
            return;
        }

        var sizes = windows.Select(w => new System.Windows.Size(
            w.ActualWidth > 0 ? w.ActualWidth : w._category.DesktopWidth,
            w.ActualHeight > 0 ? w.ActualHeight : w._category.DesktopHeight)).ToList();

        var positions = MonitorPlacement.ArrangeCentered(sizes, destArea);

        for (var i = 0; i < windows.Count && i < positions.Count; i++)
        {
            var w = windows[i];
            var pos = positions[i];

            w.PlaceWithoutSaving(pos.X, pos.Y);
            w._category.DesktopX = pos.X;
            w._category.DesktopY = pos.Y;
            w._onLayoutChanged(w._category);
        }
    }

    private void SendAllGroupsToMonitor(int targetMonitorIndex)
    {
        var areas = DisplayInventory.WorkAreas(this);
        if (targetMonitorIndex < 0 || targetMonitorIndex >= areas.Count)
        {
            return;
        }

        if (_isDockedMember && _commands is not null)
        {
            _commands.MoveDockInto(areas[targetMonitorIndex]);
            return;
        }

        var destArea = areas[targetMonitorIndex];
        var taken = new List<System.Windows.Point>();

        foreach (var window in _allGroupWindows.ToList())
        {
            var home = window.HomeRect;
            var fromIndex = MonitorPlacement.IndexOfOwner(home, areas);
            var fromArea = (fromIndex >= 0 && fromIndex < areas.Count) ? areas[fromIndex] : destArea;

            System.Windows.Point targetPos;
            if (fromIndex == targetMonitorIndex)
            {
                targetPos = MonitorPlacement.ClampInto(home.Location, home.Size, destArea);
            }
            else
            {
                targetPos = MonitorPlacement.MapBetween(home, fromArea, destArea);
            }

            targetPos = MonitorPlacement.AvoidStacking(targetPos, home.Size, destArea, taken);

            window.PlaceWithoutSaving(targetPos.X, targetPos.Y);
            window._category.DesktopX = targetPos.X;
            window._category.DesktopY = targetPos.Y;
            window._onLayoutChanged(window._category);
        }
    }

    /// <summary>
    /// Turns auto-arrange on. A group only ever holds shortcuts, so there is a single,
    /// fixed order — by shortcut name — and no sort-by menu to choose another one.
    /// </summary>
    private void EnableAutoArrange()
    {
        _category.IconArrangement = IconArrangement.ByName;
        ArrangeInGrid(NameOrder());
    }

    private IEnumerable<object> NameOrder()
    {
        var folders = _category.Categories.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase);
        var items = _category.Items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase);
        return folders.Cast<object>().Concat(items);
    }

    /// <summary>
    /// Keeps the live by-name arrangement honest after the set of icons
    /// changes underneath it — a drop, a paste, a delete, a rename. Without this, picking
    /// "auto-arrange" would only ever reflect the moment it was clicked instead of
    /// following the group as it actually stands. Free placement (<see cref="IconArrangement.None"/>)
    /// just persists and repaints, same as before this existed.
    ///
    /// Free placement used to stop at "persist" and skip the repaint — and did not even
    /// persist — so a shortcut pasted, dropped or created in a free-placement group only
    /// showed up after something else happened to rebuild the tiles (sorting, resizing,
    /// saving the settings). Both kinds now repaint and save from this one place.
    /// </summary>
    private void FinishStructuralChange()
    {
        UpdateHeaderTooltip();

        if (_category.IconArrangement != IconArrangement.None)
        {
            // Configs saved before the sort-by menu was removed may still carry Grid or
            // ByType; both now mean "by name".
            _category.IconArrangement = IconArrangement.ByName;
            ArrangeInGrid(NameOrder());
        }
        else
        {
            PopulateTiles();
            _onLayoutChanged(_category);
        }

        _overlay?.Refresh();
    }

    /// <summary>
    /// Lays entries out on a grid sized for the panel as it stands right now — its own
    /// width and the icons' own scale both feed the cell size, so a resize or a zoom
    /// change reflows the grid instead of leaving it arranged for a size that no longer
    /// applies.
    /// </summary>
    private void ArrangeInGrid(IEnumerable<object> orderedEntries)
    {
        // Positions are stored in the canvas's own pre-zoom coordinates — the render
        // transform (_zoomTransform) scales them again on screen. So only how many
        // columns fit needs the icon scale (a bigger icon needs more on-screen room,
        // meaning fewer of them per row); the spacing between those columns must stay
        // in plain, unscaled TileSize units, or the transform would apply the scale
        // twice and blow the grid past the panel's actual width.
        var usableWidth = _category.DesktopWidth - (PaddingX * 2);
        var columns = Math.Max(1, (int)(usableWidth / (HStride * _category.DesktopIconScale)));
        var index = 0;
        foreach (var entry in orderedEntries)
        {
            var x = PaddingX + ((index % columns) * HStride);
            var y = PaddingY + ((index / columns) * VStride);
            switch (entry)
            {
                case LaunchItem item:
                    item.DesktopIconX = x;
                    item.DesktopIconY = y;
                    break;
                case MenuCategory folder:
                    folder.IconX = x;
                    folder.IconY = y;
                    break;
            }

            index++;
        }

        PopulateTiles();
        _onLayoutChanged(_category);
    }

    private void SetIconScale(double scale)
    {
        _category.DesktopIconScale = scale;
        _zoomTransform.ScaleX = scale;
        _zoomTransform.ScaleY = scale;
        FinishStructuralChange();
    }

    private void AttachTileBehavior(FrameworkElement tile, object entry, Action onOpen, Action<double, double> onMoved)
    {
        _tilesByEntry[entry] = tile;
        _openActionsByEntry[entry] = onOpen;
        AttachHoverEffect(tile, entry);

        // O tile so' existe dentro do Canvas desta janela - arrastar movendo Canvas.Left/Top
        // (como era antes) faz o icone sumir assim que ele passa da borda da PROPRIA janela,
        // porque nao ha nada visivel fora dos limites de uma janela WPF. Por isso o arrasto
        // agora move uma janela-fantasma de verdade (_DragGhostWindow), em coordenadas de
        // tela, que atravessa livremente os limites de qualquer janela - exatamente como o
        // Explorer mostra um icone "flutuando" durante o arraste.
        //
        // Para nao repetir o bug ja documentado (App Folder tile sumindo por causa de
        // PointToScreen chamado duas vezes por MouseMove, misturando o DPI de antes/depois do
        // Left/Top mudar), PointToScreen so' e' chamado sobre "this" - a janela de origem, que
        // nunca se move durante o arrasto - nunca sobre a propria janela-fantasma.
        POINT dragStartPhysical = default;
        POINT initialTilePhysical = default;
        POINT grabOffsetPhysical = default;
        System.Windows.Point initialTileScreenDip = default;
        double visualWidth = 0;
        double visualHeight = 0;
        var dragging = false;
        var hasMoved = false;
        DragGhostWindow? ghost = null;
        DispatcherTimer? heartbeatTimer = null;
        DispatcherTimer? dragTrackingTimer = null;
        List<object> draggedPayload = new();
        List<FrameworkElement> dimmedTiles = new();

        void FinishDrag()
        {
            if (!dragging)
            {
                return;
            }

            dragging = false;

            dragTrackingTimer?.Stop();
            dragTrackingTimer = null;

            heartbeatTimer?.Stop();
            StopHeartbeatAnimation(tile);

            tile.ReleaseMouseCapture();

            if (!hasMoved)
            {
                ghost?.Close();
                ghost = null;
                foreach (var t in dimmedTiles)
                {
                    t.Opacity = 1.0;
                }
                dimmedTiles.Clear();

                // Se o usuário apenas clicou num item já selecionado sem arrastar,
                // reduz a seleção apenas a ele no MouseUp (comportamento padrão Windows Explorer)
                var isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                var isShift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                if (!isCtrl && !isShift && _selectedEntries.Count > 1)
                {
                    _selectedEntries.Clear();
                    _selectedEntries.Add(entry);
                    _selectionAnchor = entry;
                    RefreshSelectionVisuals();
                }
                return;
            }

            GetCursorPos(out var dropCursorPt);
            var ghostPhys = ghost is not null
                ? ghost.CurrentPhysicalTopLeft
                : new DragGhostWindow.POINT { X = initialTilePhysical.X, Y = initialTilePhysical.Y };
            ghost?.Close();
            ghost = null;

            foreach (var t in dimmedTiles)
            {
                t.Opacity = 1.0;
            }
            dimmedTiles.Clear();

            var targetGroup = FindTargetGroupWindow(dropCursorPt, ghostPhys, (int)Math.Ceiling(visualWidth), (int)Math.Ceiling(visualHeight), this);
            if (targetGroup is not null)
            {
                MoveEntriesToOtherGroup(draggedPayload, targetGroup, dropCursorPt);
                return;
            }

            // Soltura no mesmo grupo: reorganiza os elementos na lista e grade
            var targetInWindow = PointFromScreen(new System.Windows.Point(dropCursorPt.X, dropCursorPt.Y));
            var dpi = VisualTreeHelper.GetDpi(this);
            var localTopLeft = new System.Windows.Point(
                targetInWindow.X - (grabOffsetPhysical.X / dpi.DpiScaleX),
                targetInWindow.Y - (grabOffsetPhysical.Y / dpi.DpiScaleY));

            try
            {
                var transform = TransformToDescendant(_canvas);
                localTopLeft = transform.Transform(localTopLeft);
            }
            catch
            {
                var scale = _category.DesktopIconScale > 0 ? _category.DesktopIconScale : 1.0;
                localTopLeft = new System.Windows.Point(localTopLeft.X / scale, localTopLeft.Y / scale);
            }

            ReorganizeEntriesInSameGroup(draggedPayload, localTopLeft);
        }

        void UpdateDragPhysical(POINT currentCursorPt)
        {
            if (!dragging)
            {
                return;
            }

            if (!hasMoved)
            {
                var deltaX = currentCursorPt.X - dragStartPhysical.X;
                var deltaY = currentCursorPt.Y - dragStartPhysical.Y;

                if (Math.Abs(deltaX) < SystemParameters.MinimumHorizontalDragDistance
                    && Math.Abs(deltaY) < SystemParameters.MinimumVerticalDragDistance)
                {
                    return;
                }

                hasMoved = true;
                heartbeatTimer?.Stop();
                StopHeartbeatAnimation(tile);

                dimmedTiles = draggedPayload
                    .Select(item => _tilesByEntry.TryGetValue(item, out var t) ? t : null)
                    .Where(t => t != null)
                    .Cast<FrameworkElement>()
                    .ToList();

                var dpi = VisualTreeHelper.GetDpi(this);
                ghost ??= DragGhostWindow.Show(
                    tile,
                    visualWidth,
                    visualHeight,
                    initialTileScreenDip,
                    new DragGhostWindow.POINT { X = initialTilePhysical.X, Y = initialTilePhysical.Y },
                    dpi.DpiScaleX,
                    dpi.DpiScaleY,
                    draggedPayload.Count,
                    () =>
                    {
                        foreach (var t in dimmedTiles)
                        {
                            t.Opacity = SelectedTileOpacityWhileDragging;
                        }
                    });

                dragTrackingTimer?.Stop();
                dragTrackingTimer = new DispatcherTimer(DispatcherPriority.Render)
                {
                    Interval = TimeSpan.FromMilliseconds(15)
                };
                dragTrackingTimer.Tick += (_, _) =>
                {
                    if (!dragging)
                    {
                        dragTrackingTimer.Stop();
                        return;
                    }

                    var isLButtonDown = (GetAsyncKeyState(0x01) & 0x8000) != 0;
                    if (!isLButtonDown)
                    {
                        FinishDrag();
                        return;
                    }

                    if (GetCursorPos(out var pt))
                    {
                        UpdateDragPhysical(pt);
                    }
                };
                dragTrackingTimer.Start();
            }

            if (ghost is not null)
            {
                ghost.MovePhysical(currentCursorPt.X - grabOffsetPhysical.X, currentCursorPt.Y - grabOffsetPhysical.Y);
            }
        }

        tile.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                onOpen();
                e.Handled = true;
                return;
            }

            var isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            var isShift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            var isAlreadySelected = _selectedEntries.Contains(entry);

            if (isCtrl || isShift)
            {
                HandleTileSelection(entry);
            }
            else if (!isAlreadySelected)
            {
                HandleTileSelection(entry);
            }

            if (_selectedEntries.Contains(entry) && _selectedEntries.Count > 1)
            {
                draggedPayload = _selectedEntries.ToList();
            }
            else
            {
                draggedPayload = new List<object> { entry };
            }

            dragging = true;
            hasMoved = false;

            // Visual geometry in window DIP space:
            var tileTransform = tile.TransformToAncestor(this);
            var tileVisualOrigin = tileTransform.Transform(new System.Windows.Point(0, 0));
            var tileVisualBottomRight = tileTransform.Transform(new System.Windows.Point(
                tile.ActualWidth > 0 ? tile.ActualWidth : TileSize - 8,
                tile.ActualHeight > 0 ? tile.ActualHeight : TileSize));
            visualWidth = Math.Max(1, Math.Abs(tileVisualBottomRight.X - tileVisualOrigin.X));
            visualHeight = Math.Max(1, Math.Abs(tileVisualBottomRight.Y - tileVisualOrigin.Y));

            GetCursorPos(out dragStartPhysical);
            var tilePhysicalPt = tile.PointToScreen(new System.Windows.Point(0, 0));
            initialTilePhysical = new POINT
            {
                X = (int)Math.Round(tilePhysicalPt.X),
                Y = (int)Math.Round(tilePhysicalPt.Y)
            };
            grabOffsetPhysical = new POINT
            {
                X = dragStartPhysical.X - initialTilePhysical.X,
                Y = dragStartPhysical.Y - initialTilePhysical.Y
            };

            initialTileScreenDip = new System.Windows.Point(
                Left + tileVisualOrigin.X,
                Top + tileVisualOrigin.Y);

            tile.CaptureMouse();

            heartbeatTimer?.Stop();
            heartbeatTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            heartbeatTimer.Tick += (_, _) =>
            {
                heartbeatTimer.Stop();
                if (dragging && !hasMoved)
                {
                    StartHeartbeatAnimation(tile);
                }
            };
            heartbeatTimer.Start();

            e.Handled = true;
        };

        tile.MouseMove += (_, e) =>
        {
            if (!dragging)
            {
                return;
            }

            if (GetCursorPos(out var pt))
            {
                UpdateDragPhysical(pt);
            }
        };

        tile.MouseLeftButtonUp += (_, e) =>
        {
            FinishDrag();
        };

        tile.LostMouseCapture += (_, _) =>
        {
            if (dragging)
            {
                var isLButtonDown = (GetAsyncKeyState(0x01) & 0x8000) != 0;
                if (!isLButtonDown)
                {
                    FinishDrag();
                }
            }
        };
    }

    /// <summary>Opacidade do tile de origem enquanto a janela-fantasma o representa em tela - o mesmo "ícone esmaecido" que o Explorer usa durante um arrasto.</summary>
    private const double SelectedTileOpacityWhileDragging = 0.35;

    /// <summary>Localiza com precisão absoluta qual grupo de desktop está sob o cursor ou sobreposto pelo fantasma no momento da soltura.</summary>
    private static DesktopGroupWindow? FindTargetGroupWindow(
        POINT dropPt,
        DragGhostWindow.POINT ghostTopLeft,
        int ghostWidth,
        int ghostHeight,
        DesktopGroupWindow excluding)
    {
        // 1. Verificação direta por HWND sob o cursor físico
        var hwndUnderCursor = WindowFromPoint(dropPt);
        if (hwndUnderCursor != IntPtr.Zero)
        {
            var rootHwnd = GetAncestor(hwndUnderCursor, GA_ROOT);
            var match = _allGroupWindows.FirstOrDefault(w =>
                !ReferenceEquals(w, excluding) &&
                new WindowInteropHelper(w).Handle == rootHwnd);
            if (match is not null)
            {
                return match;
            }
        }

        // 2. Verificação de bounding rect físico de cada janela para o ponto do cursor
        foreach (var window in _allGroupWindows)
        {
            if (ReferenceEquals(window, excluding))
            {
                continue;
            }

            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out RECT rc))
            {
                if (dropPt.X >= rc.Left && dropPt.X <= rc.Right &&
                    dropPt.Y >= rc.Top && dropPt.Y <= rc.Bottom)
                {
                    return window;
                }
            }
        }

        // 3. Verificação de sobreposição física com o retângulo do ghost
        var ghostRect = new RECT
        {
            Left = ghostTopLeft.X,
            Top = ghostTopLeft.Y,
            Right = ghostTopLeft.X + ghostWidth,
            Bottom = ghostTopLeft.Y + ghostHeight
        };

        foreach (var window in _allGroupWindows)
        {
            if (ReferenceEquals(window, excluding))
            {
                continue;
            }

            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out RECT rc))
            {
                if (ghostRect.Left < rc.Right && ghostRect.Right > rc.Left &&
                    ghostRect.Top < rc.Bottom && ghostRect.Bottom > rc.Top)
                {
                    return window;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Reorganiza elementos soltos dentro do próprio grupo, calculando a nova posição na lista
    /// conforme o ponto onde o mouse soltou e forçando a reorganização/reflow de toda a grade.
    /// </summary>
    private void ReorganizeEntriesInSameGroup(IReadOnlyList<object> entries, System.Windows.Point dropCanvasPoint)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var usableWidth = ActualWidth > 0 ? ActualWidth - (PaddingX * 2) : _category.DesktopWidth - (PaddingX * 2);
        var scale = _category.DesktopIconScale > 0 ? _category.DesktopIconScale : 1.0;
        var columns = Math.Max(1, (int)(usableWidth / (HStride * scale)));

        var dropCol = Math.Max(0, (int)Math.Round((dropCanvasPoint.X - PaddingX) / HStride));
        var dropRow = Math.Max(0, (int)Math.Round((dropCanvasPoint.Y - PaddingY) / VStride));
        var targetSlotIndex = Math.Max(0, (dropRow * columns) + dropCol);

        var foldersToMove = entries.OfType<MenuCategory>().ToList();
        var itemsToMove = entries.OfType<LaunchItem>().ToList();

        if (foldersToMove.Count > 0)
        {
            foreach (var f in foldersToMove)
            {
                _category.Categories.Remove(f);
            }
            var insertIndex = Math.Clamp(targetSlotIndex, 0, _category.Categories.Count);
            _category.Categories.InsertRange(insertIndex, foldersToMove);
        }

        if (itemsToMove.Count > 0)
        {
            foreach (var item in itemsToMove)
            {
                _category.Items.Remove(item);
            }
            var adjustedSlot = Math.Max(0, targetSlotIndex - _category.Categories.Count);
            var insertIndex = Math.Clamp(adjustedSlot, 0, _category.Items.Count);
            _category.Items.InsertRange(insertIndex, itemsToMove);
        }

        FinishStructuralChange();

        _selectedEntries.Clear();
        foreach (var e in entries)
        {
            _selectedEntries.Add(e);
        }
        RefreshSelectionVisuals();
    }

    /// <summary>
    /// Move um ou múltiplos elementos arrastados para outro grupo de desktop, removendo-os deste
    /// grupo de origem, refazendo a grade de origem e repassando todos ao grupo alvo.
    /// </summary>
    private void MoveEntriesToOtherGroup(IReadOnlyList<object> entries, DesktopGroupWindow target, POINT dropPhysicalPoint)
    {
        if (entries.Count == 0)
        {
            return;
        }

        // What the other group will not take stays where it is: nothing is lost in a refused move.
        var (accepted, overLimit, refusedLinks) = GroupLimits.Admit(target._category, entries);
        if (overLimit > 0)
        {
            target.WarnLimit(overLimit);
        }

        if (refusedLinks > 0)
        {
            target.WarnLinkRefused();
        }

        if (accepted.Count == 0)
        {
            return;
        }

        entries = accepted;

        foreach (var entry in entries)
        {
            switch (entry)
            {
                case LaunchItem item:
                    _category.Items.Remove(item);
                    break;
                case MenuCategory folder:
                    _category.Categories.Remove(folder);
                    if (_openSubfolders.TryGetValue(folder, out var openWindow))
                    {
                        openWindow.Close();
                        _openSubfolders.Remove(folder);
                    }
                    break;
            }
        }

        FinishStructuralChange();

        var pointInTargetWindow = target.PointFromScreen(new System.Windows.Point(dropPhysicalPoint.X, dropPhysicalPoint.Y));

        System.Windows.Point dropPoint;
        try
        {
            var transform = target.TransformToDescendant(target._canvas);
            dropPoint = transform.Transform(pointInTargetWindow);
        }
        catch
        {
            var scale = target._category.DesktopIconScale > 0 ? target._category.DesktopIconScale : 1.0;
            dropPoint = new System.Windows.Point(pointInTargetWindow.X / scale, pointInTargetWindow.Y / scale);
        }

        target.AcceptMovedEntries(entries, dropPoint);
    }

    /// <summary>
    /// Recebe múltiplos itens movidos de outro grupo, inserindo-os na posição solta e
    /// forçando a reorganização dos elementos na grade do grupo alvo.
    /// </summary>
    private void AcceptMovedEntries(IReadOnlyList<object> entries, System.Windows.Point canvasPoint)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var usableWidth = ActualWidth > 0 ? ActualWidth - (PaddingX * 2) : _category.DesktopWidth - (PaddingX * 2);
        var scale = _category.DesktopIconScale > 0 ? _category.DesktopIconScale : 1.0;
        var columns = Math.Max(1, (int)(usableWidth / (HStride * scale)));

        var dropCol = Math.Max(0, (int)Math.Round((canvasPoint.X - PaddingX) / HStride));
        var dropRow = Math.Max(0, (int)Math.Round((canvasPoint.Y - PaddingY) / VStride));
        var targetSlotIndex = Math.Max(0, (dropRow * columns) + dropCol);

        var foldersToMove = entries.OfType<MenuCategory>().ToList();
        var itemsToMove = entries.OfType<LaunchItem>().ToList();

        // Where the user let go, for a group that keeps icons where they are put; the
        // coordinates they had in the source group mean nothing here. (An arranged group
        // lays them out again below anyway.)
        if (_category.IconArrangement == IconArrangement.None)
        {
            var offset = 0.0;
            foreach (var moved in entries)
            {
                var x = Math.Max(PaddingX, canvasPoint.X - ((TileSize - 8) / 2) + offset);
                var y = Math.Max(PaddingY, canvasPoint.Y - ((TileSize - 8) / 2) + offset);
                switch (moved)
                {
                    case LaunchItem item:
                        (item.DesktopIconX, item.DesktopIconY) = (x, y);
                        break;
                    case MenuCategory folder:
                        (folder.IconX, folder.IconY) = (x, y);
                        break;
                }

                offset += 16;
            }
        }

        // Arriving from another group, a name may already be taken here.
        foreach (var folder in foldersToMove)
        {
            folder.Name = GroupNames.MakeUnique(_category, folder.Name, folder);
        }

        foreach (var item in itemsToMove)
        {
            item.Name = GroupNames.MakeUnique(_category, item.Name, item);
        }

        if (foldersToMove.Count > 0)
        {
            var insertIndex = Math.Clamp(targetSlotIndex, 0, _category.Categories.Count);
            _category.Categories.InsertRange(insertIndex, foldersToMove);
        }

        if (itemsToMove.Count > 0)
        {
            var adjustedSlot = Math.Max(0, targetSlotIndex - _category.Categories.Count);
            var insertIndex = Math.Clamp(adjustedSlot, 0, _category.Items.Count);
            _category.Items.InsertRange(insertIndex, itemsToMove);
        }

        FinishStructuralChange();

        _selectedEntries.Clear();
        foreach (var e in entries)
        {
            _selectedEntries.Add(e);
        }
        RefreshSelectionVisuals();

        RestoreIfMinimized();
    }

    /// <summary>
    /// The lift a link or card gets on a web page: a soft tint plus a slight scale-up,
    /// eased in and out instead of snapping, so an icon visibly answers the mouse the
    /// instant it arrives. Left alone while the tile is selected — the selection
    /// highlight already says more than a hover tint could.
    /// </summary>
    private void AttachHoverEffect(FrameworkElement tile, object entry)
    {
        tile.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        var scale = new ScaleTransform(1, 1);
        tile.RenderTransform = scale;

        tile.MouseEnter += (_, _) =>
        {
            AnimateScale(scale, 1.08);
            if (!_selectedEntries.Contains(entry))
            {
                SetTileBackground(tile, ThemeBrushes.CreateBrush(_theme.HighlightColor, 0.45));
            }
        };

        tile.MouseLeave += (_, _) =>
        {
            AnimateScale(scale, 1.0);
            if (IsSearchActive)
            {
                ReapplySearchEmphasis();
            }
            else if (!_selectedEntries.Contains(entry))
            {
                SetTileBackground(tile, Brushes.Transparent);
            }
        };
    }

    private static void AnimateScale(ScaleTransform transform, double to)
    {
        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    private static double ClampToCanvas(double value, double tileSize, double canvasSize, double padding)
    {
        // A canvas that has not been measured yet cannot bound anything; leave the value be.
        if (double.IsNaN(value) || canvasSize <= 0)
        {
            return double.IsNaN(value) ? padding : value;
        }

        var max = Math.Max(padding, canvasSize - padding - tileSize);
        return Math.Clamp(value, padding, max);
    }

    private static void StartHeartbeatAnimation(FrameworkElement tile)
    {
        if (tile.RenderTransform is not ScaleTransform scale)
        {
            scale = new ScaleTransform(1, 1);
            tile.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            tile.RenderTransform = scale;
        }

        var anim = new DoubleAnimation(1.0, 1.07, TimeSpan.FromMilliseconds(220))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    private static void StopHeartbeatAnimation(FrameworkElement tile)
    {
        if (tile.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 1.0;
            scale.ScaleY = 1.0;
        }
    }

    private void HandleTileSelection(object entry)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (!_selectedEntries.Remove(entry))
            {
                _selectedEntries.Add(entry);
            }
            _selectionAnchor = entry;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            SelectRange(_selectionAnchor ?? entry, entry);
        }
        else
        {
            _selectedEntries.Clear();
            _selectedEntries.Add(entry);
            _selectionAnchor = entry;
        }

        RefreshSelectionVisuals();
    }

    /// <summary>The icons in the order the eye reads them: row by row, left to right, by where they are drawn.</summary>
    private List<object> VisualOrder()
    {
        var entries = _tilesByEntry.Keys.ToList();
        var positions = entries.Select(PositionOf).ToList();
        return TileNavigation.ReadingOrder(positions, TileSize * 0.5).Select(i => entries[i]).ToList();
    }

    private (double X, double Y) PositionOf(object entry)
    {
        var tile = _tilesByEntry[entry];
        var x = Canvas.GetLeft(tile);
        var y = Canvas.GetTop(tile);
        return (double.IsNaN(x) ? 0 : x, double.IsNaN(y) ? 0 : y);
    }

    /// <summary>The entry a navigation key reaches from the current one, by screen position, wrapping at both ends.</summary>
    private object? NavigateFrom(object? current, NavigationKey key)
    {
        var entries = _tilesByEntry.Keys.ToList();
        if (entries.Count == 0)
        {
            return null;
        }

        var scale = _category.DesktopIconScale > 0 ? _category.DesktopIconScale : 1.0;
        var viewport = _scroller.ViewportHeight > 0 ? _scroller.ViewportHeight : _scroller.ActualHeight;
        var pageRows = Math.Max(1, (int)(viewport / (VStride * scale)));
        var index = TileNavigation.Move(
            entries.Select(PositionOf).ToList(),
            current is null ? -1 : entries.IndexOf(current),
            key,
            pageRows,
            TileSize * 0.5);
        return index < 0 ? null : entries[index];
    }

    private object? _navigationCursor;

    private void SelectFirstVisual()
    {
        if (VisualOrder().FirstOrDefault() is { } first)
        {
            _navigationCursor = first;
            SelectEntry(first, additive: false);
        }
    }

    private void SelectRange(object fromEntry, object toEntry)
    {
        var all = VisualOrder();
        var i1 = all.IndexOf(fromEntry);
        var i2 = all.IndexOf(toEntry);
        if (i1 < 0) i1 = 0;
        if (i2 < 0) i2 = all.Count - 1;

        var start = Math.Min(i1, i2);
        var end = Math.Max(i1, i2);

        _selectedEntries.Clear();
        for (var i = start; i <= end; i++)
        {
            _selectedEntries.Add(all[i]);
        }

        RefreshSelectionVisuals();
    }

    private void SelectEntry(object entry, bool additive)
    {
        if (additive)
        {
            if (!_selectedEntries.Remove(entry))
            {
                _selectedEntries.Add(entry);
            }
            _selectionAnchor = entry;
        }
        else
        {
            _selectedEntries.Clear();
            _selectedEntries.Add(entry);
            _selectionAnchor = entry;
        }

        RefreshSelectionVisuals();
        ScrollEntryIntoView(entry);
    }

    private void ClearSelection()
    {
        if (_selectedEntries.Count == 0)
        {
            return;
        }

        _selectedEntries.Clear();
        RefreshSelectionVisuals();
    }

    private void RefreshSelectionVisuals()
    {
        foreach (var (entry, tile) in _tilesByEntry)
        {
            SetTileBackground(tile, _selectedEntries.Contains(entry)
                ? ThemeBrushes.CreateBrush(_theme.HighlightColor, 1.0)
                : Brushes.Transparent);
        }

        UpdateHeaderTitle();
    }

    /// <summary>
    /// "Group [Icon]" while one icon is selected, "Group [3]" for several, plain "Group" for none.
    /// The window's own Title stays the bare group name.
    /// </summary>
    private void UpdateHeaderTitle()
    {
        if (_headerText is null)
        {
            return;
        }

        _headerText.Text = _selectedEntries.Count switch
        {
            0 => _category.Name,
            1 => $"{_category.Name} [{GroupEntries.NameOf(_selectedEntries.First())}]",
            var n => $"{_category.Name} [{n}]"
        };
    }

    private void RemoveSelectedEntries()
    {
        RemoveEntries(_category, _selectedEntries.ToList(), this);
    }

    private void RenameSelectedEntry()
    {
        if (_selectedEntries.FirstOrDefault() is { } entry)
        {
            RenameEntry(_category, entry, this);
        }
    }

    private static string GetEntryName(object entry) => entry switch
    {
        LaunchItem item => item.Name,
        MenuCategory folder => folder.Name,
        _ => string.Empty
    };

    /// <summary>
    /// The icon's hit area and its highlight: a rounded plate, so the selection, hover and search
    /// emphasis read as a soft tile rather than a hard rectangle.
    /// </summary>
    private static Border WrapTile(StackPanel content, ContextMenu? menu) => new()
    {
        Width = TileSize - 8,
        CornerRadius = new CornerRadius(10),
        Background = Brushes.Transparent,
        Cursor = Cursors.Hand,
        Child = content,
        ContextMenu = menu
    };

    private static void SetTileBackground(FrameworkElement tile, Brush brush)
    {
        if (tile is Border border)
        {
            border.Background = brush;
        }
    }

    private void AddTile(LaunchItem item, double x, double y)
    {
        var tile = BuildTile(item);
        Canvas.SetLeft(tile, x);
        Canvas.SetTop(tile, y);
        _canvas.Children.Add(tile);
    }

    private FrameworkElement BuildTile(LaunchItem item)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        var tile = WrapTile(stack, null);

        // Rebuilt per click: the menu acts on the selection as it is now, and a right-click on an
        // icon outside the selection selects it first, as Explorer does.
        tile.PreviewMouseRightButtonDown += (_, _) =>
        {
            if (!_selectedEntries.Contains(item))
            {
                SelectEntry(item, additive: false);
            }

            tile.ContextMenu = BuildItemTileContextMenu(item);
        };

        var isGroupLink = item.Type == LaunchItemType.GroupLink;
        var icon = isGroupLink ? null : _iconCache.GetIcon(item.IconOverridePath ?? item.Target);
        if (icon is not null)
        {
            stack.Children.Add(new Image
            {
                Source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()),
                Width = _theme.IconSize * 1.6,
                Height = _theme.IconSize * 1.6,
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }
        else if (isGroupLink)
        {
            stack.Children.Add(new Border
            {
                Height = _theme.IconSize * 1.6,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = AppIcons.Create("IconStylePanel", TileTextBrush(), _theme.IconSize * 1.4)
            });
        }
        else
        {
            // Never an empty slot: a caption with no picture above it reads as "the icon
            // vanished". A warning glyph says the shortcut itself is the problem.
            stack.Children.Add(new TextBlock
            {
                Text = "",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = _theme.IconSize * 1.4,
                Height = _theme.IconSize * 1.6,
                Foreground = TileTextBrush(),
                Opacity = 0.75,
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }

        if (IsTargetMissing(item))
        {
            tile.ToolTip = LocalizationService.Format("item.targetMissing", item.Target);
        }

        var caption = new TextBlock
        {
            Text = item.Name,
            Foreground = TileTextBrush(),
            FontFamily = new FontFamily(_theme.ItemFontFamily),
            FontSize = _theme.ItemFontSize,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = TileTextShadow()
        };
        stack.Children.Add(caption);
        _captionsByEntry[item] = caption;

        AttachTileBehavior(
            tile,
            item,
            () => _onExecute(item),
            (x, y) =>
            {
                if (_category.IconArrangement != IconArrangement.None)
                {
                    FinishStructuralChange();
                }
                else
                {
                    _category.IconArrangement = IconArrangement.None;
                    item.DesktopIconX = x;
                    item.DesktopIconY = y;
                    _onLayoutChanged(_category);
                }
            });

        return tile;
    }

    /// <summary>True when the item names a file or folder on disk that is no longer there.</summary>
    internal static bool IsTargetMissing(LaunchItem item)
    {
        if (item.Type is LaunchItemType.Url or LaunchItemType.Command
            || string.IsNullOrWhiteSpace(item.Target)
            || item.Target.Contains("://")
            || !Path.IsPathRooted(item.Target))
        {
            return false;
        }

        return IconCacheService.ResolveFullPath(item.Target) is null;
    }

    /// <summary>Points a shortcut whose file disappeared at another one, chosen by the user.</summary>
    private void LocateMissingTarget(LaunchItem item)
    {
        var dialog = new OpenFileDialog
        {
            DereferenceLinks = false,
            Title = LocalizationService.Format("item.locateTargetTitle", item.Name),
            Filter = LocalizationService.Get("group.importShortcutsFilter")
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        item.Target = ShortcutStore.Adopt(dialog.FileName);
        item.Type = InferType(dialog.FileName);
        FinishStructuralChange();
    }

    private void AddFolderTile(MenuCategory folder, double x, double y)
    {
        var tile = BuildFolderTile(folder);
        Canvas.SetLeft(tile, x);
        Canvas.SetTop(tile, y);
        _canvas.Children.Add(tile);
    }

    private FrameworkElement BuildFolderTile(MenuCategory folder)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        var tile = WrapTile(stack, null);
        // The "Remove" item's enabled state depends on the selection at the moment of
        // the click, not whenever the tile last happened to be rebuilt — rebuild the
        // menu fresh right before it opens rather than let that state go stale.
        tile.PreviewMouseRightButtonDown += (_, _) =>
        {
            if (!_selectedEntries.Contains(folder))
            {
                SelectEntry(folder, additive: false);
            }

            tile.ContextMenu = BuildFolderTileContextMenu(folder);
        };

        stack.Children.Add(new TextBlock
        {
            Text = "",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = _theme.IconSize * 1.6,
            Foreground = TileTextBrush(),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        var caption = new TextBlock
        {
            Text = folder.Name,
            Foreground = TileTextBrush(),
            FontFamily = new FontFamily(_theme.ItemFontFamily),
            FontSize = _theme.ItemFontSize,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = TileTextShadow()
        };
        stack.Children.Add(caption);
        _captionsByEntry[folder] = caption;

        AttachTileBehavior(
            tile,
            folder,
            () => OpenSubfolder(folder),
            (x, y) =>
            {
                if (_category.IconArrangement != IconArrangement.None)
                {
                    FinishStructuralChange();
                }
                else
                {
                    _category.IconArrangement = IconArrangement.None;
                    folder.IconX = x;
                    folder.IconY = y;
                    _onLayoutChanged(_category);
                }
            });

        return tile;
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        // Shown only while the button is held: hovering the title is not a move.
        _header.Cursor = Cursors.SizeAll;
        _dockDragging = _isDockedMember;
        try
        {
            DragMove();
        }
        finally
        {
            _dockDragging = false;
            _header.Cursor = Cursors.Arrow;
        }

        KeepOnScreen(byCursor: true);
        if (_isDockedMember && _commands is not null)
        {
            _commands.DockMovedTo(_category, Left, Top);
            return;
        }

        _category.DesktopX = Left;
        _category.DesktopY = Top;
        _monitorPositions.Clear();
        _onLayoutChanged(_category);
    }

    private void SetResizeHandlesVisible(bool visible)
    {
        var visibility = visible && !_isDockedMember ? Visibility.Visible : Visibility.Collapsed;
        foreach (var handle in _resizeHandles)
        {
            handle.Visibility = visibility;
        }
    }

    private void OnResizeMouseDown(ResizeEdge edge, UIElement source, MouseButtonEventArgs e)
    {
        _resizeEdge = edge;
        _resizeOrigin = PointToScreen(e.GetPosition(this));
        _resizeStartLeft = Left;
        _resizeStartTop = Top;
        _resizeStartWidth = Width;
        _resizeStartHeight = Height;
        source.CaptureMouse();
        e.Handled = true;
    }

    private void OnResizeMouseMove(object sender, MouseEventArgs e)
    {
        if (_resizeEdge == ResizeEdge.None)
        {
            return;
        }

        var current = PointToScreen(e.GetPosition(this));
        var deltaX = current.X - _resizeOrigin.X;
        var deltaY = current.Y - _resizeOrigin.Y;

        var growsFromLeft = _resizeEdge is ResizeEdge.W or ResizeEdge.NW or ResizeEdge.SW;
        var growsFromTop = _resizeEdge is ResizeEdge.N or ResizeEdge.NE or ResizeEdge.NW;

        var newWidth = _resizeStartWidth;
        if (_resizeEdge is ResizeEdge.E or ResizeEdge.NE or ResizeEdge.SE)
        {
            newWidth = _resizeStartWidth + deltaX;
        }
        else if (growsFromLeft)
        {
            newWidth = _resizeStartWidth - deltaX;
        }

        var newHeight = _resizeStartHeight;
        if (_resizeEdge is ResizeEdge.S or ResizeEdge.SE or ResizeEdge.SW)
        {
            newHeight = _resizeStartHeight + deltaY;
        }
        else if (growsFromTop)
        {
            newHeight = _resizeStartHeight - deltaY;
        }

        newWidth = Math.Max(MinGroupWidth, newWidth);
        newHeight = Math.Max(MinGroupHeight, newHeight);

        // The opposite edge is the anchor: it must not move while this one is dragged.
        var newLeft = growsFromLeft ? _resizeStartLeft + _resizeStartWidth - newWidth : _resizeStartLeft;
        var newTop = growsFromTop ? _resizeStartTop + _resizeStartHeight - newHeight : _resizeStartTop;

        Width = newWidth;
        Height = newHeight;
        Left = newLeft;
        Top = newTop;
        _category.DesktopWidth = newWidth;
        _category.DesktopHeight = newHeight;
        _category.DesktopX = newLeft;
        _category.DesktopY = newTop;

        if (_category.IconArrangement != IconArrangement.None)
        {
            ReflowGridDuringResize();
        }
    }

    private void ReflowGridDuringResize()
    {
        var usableWidth = _category.DesktopWidth - (PaddingX * 2);
        var columns = Math.Max(1, (int)(usableWidth / (HStride * _category.DesktopIconScale)));
        var ordered = NameOrder();

        var index = 0;
        foreach (var entry in ordered)
        {
            var x = PaddingX + ((index % columns) * HStride);
            var y = PaddingY + ((index / columns) * VStride);
            switch (entry)
            {
                case LaunchItem item:
                    item.DesktopIconX = x;
                    item.DesktopIconY = y;
                    break;
                case MenuCategory folder:
                    folder.IconX = x;
                    folder.IconY = y;
                    break;
            }

            if (_tilesByEntry.TryGetValue(entry, out var tile))
            {
                Canvas.SetLeft(tile, x);
                Canvas.SetTop(tile, y);
            }

            index++;
        }

        UpdateCanvasExtent();
    }

    private void OnResizeMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_resizeEdge == ResizeEdge.None)
        {
            return;
        }

        _resizeEdge = ResizeEdge.None;
        ((UIElement)sender).ReleaseMouseCapture();
        KeepOnScreen(byCursor: false);
        _category.DesktopWidth = Width;
        _category.DesktopHeight = Height;
        _category.DesktopX = Left;
        _category.DesktopY = Top;
        FinishStructuralChange();
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        ApplyZoomDelta(e.Delta > 0 ? 0.1 : -0.1);
        e.Handled = true;
    }

    /// <summary>
    /// Explorer-style type-to-select: with nothing else capturing the keyboard, typing a
    /// name jumps the selection to the first icon whose name starts with it, accumulating
    /// characters within <see cref="TypeAheadTimeout"/> of each other and starting over on
    /// the next keystroke after a pause. This is separate from Ctrl+F — no box appears,
    /// nothing is highlighted inside the name, it is just "start typing, land on the icon".
    /// </summary>
    private void OnPreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        if (_searchBar.Visibility == Visibility.Visible)
        {
            return;
        }

        if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0]))
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _typeAheadLastInput > TypeAheadTimeout)
        {
            _typeAheadBuffer = string.Empty;
        }

        _typeAheadLastInput = now;
        _typeAheadBuffer += e.Text;

        var entries = GroupEntries.Enumerate(_category).ToList();
        var match = entries.FirstOrDefault(entry =>
            GroupEntries.NameOf(entry).StartsWith(_typeAheadBuffer, StringComparison.CurrentCultureIgnoreCase));

        if (match is null && _typeAheadBuffer.Length > 1)
        {
            // No entry continues the accumulated buffer — start over from just this
            // keystroke instead, the same recovery Explorer's own type-ahead does.
            _typeAheadBuffer = e.Text;
            match = entries.FirstOrDefault(entry =>
                GroupEntries.NameOf(entry).StartsWith(_typeAheadBuffer, StringComparison.CurrentCultureIgnoreCase));
        }

        if (match is not null)
        {
            SelectEntry(match, additive: false);
            e.Handled = true;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // While the find bar is open it owns the keyboard outright: everything from
        // plain letters (typed into the search box further down the tunnel) to Escape
        // and the result list's own Up/Down/Enter is decided here first.
        if (_searchBar.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape)
            {
                CloseFindOverlay(rememberQuery: true);
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                MoveSearchSelection(1);
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                MoveSearchSelection(-1);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                ActivateSelectedSearchResult();
                e.Handled = true;
            }

            return;
        }

        // The same shortcut Windows uses to send a window to another monitor.
        if (e.Key is Key.Left or Key.Right
            && Keyboard.Modifiers == (ModifierKeys.Windows | ModifierKeys.Shift))
        {
            MoveToAdjacentMonitor(e.Key == Key.Left ? -1 : +1);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key is Key.OemPlus or Key.Add)
            {
                ApplyZoomDelta(0.1);
                e.Handled = true;
            }
            else if (e.Key is Key.OemMinus or Key.Subtract)
            {
                ApplyZoomDelta(-0.1);
                e.Handled = true;
            }
            else if (e.Key == Key.A)
            {
                SelectAllEntries();
                e.Handled = true;
            }
            else if (e.Key == Key.C)
            {
                CopySelectedToClipboard(cut: false);
                e.Handled = true;
            }
            else if (e.Key == Key.X)
            {
                CopySelectedToClipboard(cut: true);
                e.Handled = true;
            }
            else if (e.Key == Key.V)
            {
                PasteFromClipboard();
                e.Handled = true;
            }
            else if (e.Key == Key.F)
            {
                OpenFindOverlay();
                e.Handled = true;
            }

            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            if (e.Key == Key.F5)
            {
                FinishStructuralChange();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                ClearSelection();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                if (_selectedEntries.Count > 0)
                {
                    foreach (var entry in _selectedEntries.ToList())
                    {
                        if (_openActionsByEntry.TryGetValue(entry, out var open))
                        {
                            open();
                        }
                    }

                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.Delete)
            {
                RemoveSelectedEntries();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F2 && _selectedEntries.Count == 1)
            {
                RenameSelectedEntry();
                e.Handled = true;
                return;
            }
        }

        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown
            && (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Shift))
        {
            var key = e.Key switch
            {
                Key.Left => NavigationKey.Left,
                Key.Right => NavigationKey.Right,
                Key.Up => NavigationKey.Up,
                Key.Down => NavigationKey.Down,
                Key.Home => NavigationKey.Home,
                Key.End => NavigationKey.End,
                Key.PageUp => NavigationKey.PageUp,
                _ => NavigationKey.PageDown
            };

            var current = _navigationCursor is not null && _tilesByEntry.ContainsKey(_navigationCursor) && _selectedEntries.Contains(_navigationCursor)
                ? _navigationCursor
                : _selectedEntries.FirstOrDefault(_tilesByEntry.ContainsKey);

            if (NavigateFrom(current, key) is { } target)
            {
                _navigationCursor = target;
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    SelectRange(_selectionAnchor ?? current ?? target, target);
                }
                else
                {
                    _selectedEntries.Clear();
                    _selectedEntries.Add(target);
                    _selectionAnchor = target;
                    RefreshSelectionVisuals();
                }

                ScrollEntryIntoView(target);
                e.Handled = true;
                return;
            }
        }
    }

    private void SelectAllEntries()
    {
        _selectedEntries.Clear();
        foreach (var entry in GroupEntries.Enumerate(_category))
        {
            _selectedEntries.Add(entry);
        }

        RefreshSelectionVisuals();
    }

    /// <summary>
    /// Puts the selected tiles' underlying files on the Windows clipboard as a real
    /// file drop, so Ctrl+V works here and in Explorer alike. Subfolders are skipped:
    /// they are this app's own grouping, not a real folder on disk, so there is nothing
    /// to hand the clipboard.
    /// </summary>
    private void CopySelectedToClipboard(bool cut) => CopyEntriesToClipboard(_selectedEntries.ToList(), cut);

    internal void CopyEntriesToClipboard(IReadOnlyCollection<object> entries, bool cut)
    {
        var candidates = entries
            .OfType<LaunchItem>()
            .Select(item => (item, path: ShellCommands.TryResolveTarget(item, out var resolved) ? resolved : null))
            .Where(x => x.path is not null)
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        var data = new System.Windows.DataObject();
        data.SetData(System.Windows.DataFormats.FileDrop, candidates.Select(x => x.path!).ToArray());
        // The convention Explorer itself uses to tell a paste-as-copy from a paste-as-move.
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 5)));

        try
        {
            System.Windows.Clipboard.SetDataObject(data, true);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another process owns the clipboard right now; nothing to recover.
            return;
        }

        // Whatever was pending before belongs to a clipboard state that no longer
        // exists — this copy or cut just replaced it.
        ClearPendingCuts();

        if (!cut)
        {
            return;
        }

        foreach (var (item, path) in candidates)
        {
            _pendingCuts.Add(new PendingCut(this, item, path!));
        }

        RefreshCutVisuals();
        StartPendingCutWatcher();
    }

    /// <summary>Accepts files copied or cut anywhere in Windows the same way a drag-drop does.</summary>
    private void PasteFromClipboard()
    {
        if (!System.Windows.Clipboard.ContainsFileDropList())
        {
            return;
        }

        var paths = System.Windows.Clipboard.GetFileDropList()
            .Cast<string>()
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();

        if (paths.Length == 0)
        {
            return;
        }

        // A cut from another group of this app is a move, not a second copy: let the
        // source go first, so the name it held is free for the item arriving here.
        FinalizePendingCuts(paths);
        AddIncomingPaths(paths);
    }

    private sealed record PendingCut(DesktopGroupWindow Owner, LaunchItem Item, string Path);

    private static readonly List<PendingCut> _pendingCuts = new();
    private static DispatcherTimer? _pendingCutWatcher;

    /// <summary>Half-opacity, the same way Explorer dims an icon between Cut and Paste.</summary>
    private void RefreshCutVisuals()
    {
        foreach (var (entry, tile) in _tilesByEntry)
        {
            tile.Opacity = _pendingCuts.Any(p => ReferenceEquals(p.Owner, this) && ReferenceEquals(p.Item, entry))
                ? 0.5
                : 1.0;
        }
    }

    private static void ClearPendingCuts()
    {
        if (_pendingCuts.Count == 0)
        {
            return;
        }

        var owners = _pendingCuts.Select(p => p.Owner).Distinct().ToList();
        _pendingCuts.Clear();
        foreach (var owner in owners)
        {
            owner.RefreshCutVisuals();
        }

        StopPendingCutWatcherIfIdle();
    }

    private static void FinalizePendingCuts(IReadOnlyCollection<string> pastedPaths)
    {
        var matched = _pendingCuts
            .Where(p => pastedPaths.Contains(p.Path, StringComparer.OrdinalIgnoreCase))
            .ToList();

        RemovePendingCuts(matched);
    }

    private static void RemovePendingCuts(List<PendingCut> pending)
    {
        if (pending.Count == 0)
        {
            return;
        }

        foreach (var group in pending.GroupBy(p => p.Owner))
        {
            foreach (var entry in group)
            {
                group.Key._category.Items.Remove(entry.Item);
                _pendingCuts.Remove(entry);
            }

            group.Key.FinishStructuralChange();
        }

        StopPendingCutWatcherIfIdle();
    }

    private static void StartPendingCutWatcher()
    {
        if (_pendingCutWatcher is not null)
        {
            return;
        }

        // Real Explorer never tells us a cut file was pasted elsewhere and physically
        // moved — the only outside signal available is that the file stops existing at
        // the path it was cut from. Polling is the only option; a couple of seconds of
        // lag before the icon disappears is an acceptable trade for not hooking the shell.
        _pendingCutWatcher = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pendingCutWatcher.Tick += (_, _) => CheckPendingCutsAgainstDisk();
        _pendingCutWatcher.Start();
    }

    private static void CheckPendingCutsAgainstDisk()
    {
        var gone = _pendingCuts
            .Where(p => !File.Exists(p.Path) && !Directory.Exists(p.Path))
            .ToList();

        RemovePendingCuts(gone);
    }

    private static void StopPendingCutWatcherIfIdle()
    {
        if (_pendingCuts.Count > 0 || _pendingCutWatcher is null)
        {
            return;
        }

        _pendingCutWatcher.Stop();
        _pendingCutWatcher = null;
    }

    private void ApplyZoomDelta(double delta)
    {
        var newScale = Math.Clamp(_category.DesktopIconScale + delta, MinIconScale, MaxIconScale);
        if (Math.Abs(newScale - _category.DesktopIconScale) < 0.001)
        {
            return;
        }

        SetIconScale(newScale);
    }
}
