using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Media;
using SmartDockGroups.App.Desktop;
using SmartDockGroups.App.Localization;
using SmartDockGroups.App.Menu;
using SmartDockGroups.App.Services;
using SmartDockGroups.App.Settings;
using SmartDockGroups.App.Theming;
using SmartDockGroups.Core.Configuration;
using SmartDockGroups.Core.Models;
using Application = System.Windows.Application;

namespace SmartDockGroups.App;

public partial class App : Application, IDesktopGroupCommands
{
    private ConfigurationStore? _configurationStore;
    private LauncherConfiguration? _configuration;
    private IconCacheService? _iconCache;
    private NotifyIcon? _trayIcon;
    private Window? _menuHost;
    private GlobalHotkeyService? _hotkeyService;
    private DesktopOrganizerService? _desktopOrganizer;
    private SettingsWindow? _settingsWindow;
    private SingleInstanceCoordinator? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new SingleInstanceCoordinator();
        if (!_singleInstance.IsFirstInstance)
        {
            // This launch exists only to carry a desktop context-menu verb (or a stray
            // double-click) to the instance that is already running.
            var forwardedAction = DesktopContextMenuRegistration.ParseAction(e.Args);
            if (forwardedAction is not null)
            {
                SingleInstanceCoordinator.TrySendToRunningInstance(forwardedAction);
            }

            _singleInstance.Dispose();
            Shutdown();
            return;
        }

        _configurationStore = new ConfigurationStore(ApplicationPaths.ConfigFilePath);
        _configuration = _configurationStore.Load();
        var idsAdded = DesktopOrganizerService.EnsureIds(_configuration);

        // Rewrites item targets and names, so the file as it was is kept beside it first.
        var repaired = ShortcutStore.AdoptAndRecoverAll(_configuration) | GroupNames.EnsureUnique(_configuration);
        if (repaired)
        {
            BackupConfiguration("before-repair");
        }

        if (idsAdded || repaired)
        {
            _configurationStore.Save(_configuration);
        }

        foreach (var group in DesktopOrganizerService.AllDesktopGroups(_configuration))
        {
            if (group.Id is { } groupId)
            {
                _shortcutNames[groupId] = group.Name;
            }
        }

        _iconCache = new IconCacheService(ApplicationPaths.IconCacheDirectory);

        AppThemeService.Apply(_configuration.Behavior.AppTheme);
        LocalizationService.SetLanguage(
            string.IsNullOrEmpty(_configuration.Behavior.Language)
                ? LocalizationService.DetectLanguage()
                : _configuration.Behavior.Language);

        _menuHost = new Window
        {
            Width = 0,
            Height = 0,
            Left = -10000,
            Top = -10000,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent
        };
        _menuHost.Show();

        _hotkeyService = new GlobalHotkeyService(_menuHost);
        _hotkeyService.HotkeyPressed += ShowTrayMenu;
        _hotkeyService.RestoreGroupsRequested += () => _desktopOrganizer?.RestoreMinimizedGroups();
        _hotkeyService.Apply(_configuration.Behavior);

        _desktopOrganizer = new DesktopOrganizerService(_iconCache);
        RefreshDesktopGroups();
        _desktopOrganizer.StartWatchingDisplays();

        _trayIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Visible = true,
            Text = LocalizationService.Get("common.appName")
        };

        _trayIcon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Right)
            {
                ShowTrayMenu();
                return;
            }

            if (args.Button == MouseButtons.Left && _configuration.Behavior.ClickMode == TrayClickMode.SingleClick)
            {
                ShowTrayMenu();
            }
        };

        _trayIcon.MouseDoubleClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left && _configuration.Behavior.ClickMode == TrayClickMode.DoubleClick)
            {
                ShowTrayMenu();
            }
        };

        if (!ApplicationPaths.IsIsolatedInstance)
        {
            DesktopContextMenuRegistration.Register();
        }
        _singleInstance.StartListening(action => Dispatcher.Invoke(() => HandleDesktopAction(action)));

        // This very launch can itself carry a verb — the desktop context menu when the
        // app was closed relaunches it directly rather than going through the pipe.
        var startupAction = DesktopContextMenuRegistration.ParseAction(e.Args);
        if (startupAction is not null)
        {
            HandleDesktopAction(startupAction);
        }
    }

    /// <summary>A dated copy of config.json next to it. Best-effort: failing to back up never blocks startup.</summary>
    private static void BackupConfiguration(string reason)
    {
        try
        {
            var source = ApplicationPaths.ConfigFilePath;
            if (File.Exists(source))
            {
                File.Copy(source, $"{source}.{reason}-{DateTime.Now:yyyyMMdd-HHmmss}.bak", overwrite: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[App] Config backup failed: {ex.Message}");
        }
    }

    private void HandleDesktopAction(string action)
    {
        if (action.StartsWith(DesktopContextMenuRegistration.FocusGroupActionPrefix, StringComparison.Ordinal))
        {
            FocusGroup(action[DesktopContextMenuRegistration.FocusGroupActionPrefix.Length..]);
            return;
        }

        switch (action)
        {
            case DesktopContextMenuRegistration.NewGroupAction:
                CreateDesktopGroup();
                break;
            case DesktopContextMenuRegistration.OpenAllGroupsAction:
                _desktopOrganizer?.OpenAllGroups(_configuration!);
                SaveAndReloadGroups();
                break;
            case DesktopContextMenuRegistration.CloseAllGroupsAction:
                _desktopOrganizer?.CloseAllGroups(_configuration!);
                SaveAndReloadGroups();
                break;
            case DesktopContextMenuRegistration.ToggleCollapseAllAction:
                _desktopOrganizer?.ToggleCollapseAll();
                break;
            case DesktopContextMenuRegistration.AllAppFolderAction:
                SetAllGroupsDisplayMode(DesktopGroupDisplayMode.AppFolder);
                break;
            case DesktopContextMenuRegistration.AllPanelAction:
                SetAllGroupsDisplayMode(DesktopGroupDisplayMode.Panel);
                break;
            case DesktopContextMenuRegistration.OpenSettingsAction:
                OpenSettingsWindow();
                break;
        }
    }

    private void SetAllGroupsDisplayMode(DesktopGroupDisplayMode mode)
    {
        foreach (var group in DesktopOrganizerService.AllDesktopGroups(_configuration!))
        {
            group.DisplayMode = mode;
        }

        SaveAndReloadGroups();
    }

    /// <summary>
    /// The app icon at the size the tray asks for. Picking the frame by the system's
    /// small-icon size, rather than letting it scale the largest one, is what keeps the
    /// tile gaps crisp on a high-DPI tray.
    /// </summary>
    private static System.Drawing.Icon LoadTrayIcon()
    {
        var resource = GetResourceStream(new Uri("pack://application:,,,/Assets/SmartDockGroups.ico"));
        if (resource is null)
        {
            return System.Drawing.SystemIcons.Application;
        }

        using var stream = resource.Stream;
        return new System.Drawing.Icon(stream, SystemInformation.SmallIconSize);
    }

    private readonly Dictionary<string, string> _shortcutNames = new();

    private void OnDesktopGroupLayoutChanged(MenuCategory category)
    {
        _configurationStore!.Save(_configuration!);

        // A renamed group renames its Start-menu shortcut (only if it has one). Checked on name
        // change only: this callback fires for every move and resize as well.
        if (category.Id is { } id
            && (!_shortcutNames.TryGetValue(id, out var known) || !string.Equals(known, category.Name, StringComparison.Ordinal)))
        {
            _shortcutNames[id] = category.Name;
            if (Environment.ProcessPath is { } exe)
            {
                TaskbarShortcutService.SyncName(category, exe);
            }
        }
    }

    private void RefreshDesktopGroups()
    {
        _desktopOrganizer!.Refresh(
            _configuration!,
            OnDesktopGroupLayoutChanged,
            OnDesktopGroupDeleteRequested,
            this);
    }

    /// <summary>Writes the configuration out and repaints whatever is already on screen.</summary>
    private void SaveAndReloadGroups()
    {
        _configurationStore!.Save(_configuration!);
        RefreshDesktopGroups();
        _desktopOrganizer!.ReloadVisuals(_configuration!);
    }

    private void FocusGroup(string groupId)
    {
        var group = DesktopOrganizerService.FindGroupById(_configuration!, groupId);
        if (group is null)
        {
            _trayIcon?.ShowBalloonTip(3000, LocalizationService.Get("common.appName"), LocalizationService.Get("group.notFound"), ToolTipIcon.Warning);
            return;
        }

        if (group.IsClosed)
        {
            group.IsClosed = false;
            SaveAndReloadGroups();
        }

        _desktopOrganizer!.FocusOpenGroup(group);
    }

    void IDesktopGroupCommands.CreateTaskbarShortcut(MenuCategory source)
    {
        if (DesktopOrganizerService.EnsureIds(_configuration!))
        {
            _configurationStore!.Save(_configuration!);
        }

        var exePath = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path.");
        var shortcut = TaskbarShortcutService.CreateGroupShortcut(source, exePath);
        TaskbarShortcutService.RevealInExplorer(shortcut);
        _trayIcon?.ShowBalloonTip(10000, LocalizationService.Get("common.appName"), LocalizationService.Get("group.taskbarShortcutHint"), ToolTipIcon.Info);
    }

    void IDesktopGroupCommands.Duplicate(MenuCategory source)
    {
        var parent = DesktopOrganizerService.FindParentList(_configuration!, source) ?? _configuration!.Categories;

        var copy = source.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = LocalizationService.Format("group.copySuffix", source.Name);

        // Offset so the copy is visibly a second group rather than hiding the original.
        copy.DesktopX = source.DesktopX + 28;
        copy.DesktopY = source.DesktopY + 28;

        parent.Add(copy);
        SaveAndReloadGroups();
    }

    void IDesktopGroupCommands.ShareVisual(MenuCategory source, VisualAspects aspects, bool asDefault)
    {
        if (!asDefault)
        {
            foreach (var group in DesktopOrganizerService.AllDesktopGroups(_configuration!))
            {
                if (!ReferenceEquals(group, source))
                {
                    group.CopyVisualFrom(source, aspects, _configuration!.Theme);
                }
            }

            SaveAndReloadGroups();
            return;
        }

        if (aspects.HasFlag(VisualAspects.BackgroundColor))
        {
            // The default theme is also what every group without an override of its own
            // is showing right now. Those groups keep their current look explicitly, so
            // that only groups created from here on pick up the new default.
            var previous = _configuration!.Theme;
            foreach (var group in DesktopOrganizerService.AllDesktopGroups(_configuration))
            {
                group.ThemeOverride ??= previous.Clone();
            }

            var sourceTheme = source.ThemeOverride ?? previous;
            if (aspects == VisualAspects.All)
            {
                _configuration.Theme = sourceTheme.Clone();
            }
            else
            {
                _configuration.Theme = previous.Clone();
                _configuration.Theme.BackgroundColor = sourceTheme.BackgroundColor;
                _configuration.Theme.TextColor = sourceTheme.TextColor;
            }
        }

        _configuration!.GroupDefaults.TakeFrom(source, aspects);
        SaveAndReloadGroups();
    }

    void IDesktopGroupCommands.ApplyWallpaper(MenuCategory? target)
    {
        var wallpaper = WallpaperService.TryGetCurrentWallpaper();
        if (wallpaper is null)
        {
            System.Windows.MessageBox.Show(
                LocalizationService.Get("group.wallpaperUnavailable"),
                LocalizationService.Get("common.appName"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (target is not null)
        {
            target.DesktopBackgroundImagePath = wallpaper;
        }
        else
        {
            foreach (var group in DesktopOrganizerService.AllDesktopGroups(_configuration!))
            {
                group.DesktopBackgroundImagePath = wallpaper;
            }
        }

        SaveAndReloadGroups();
    }

    void IDesktopGroupCommands.OpenSettings()
    {
        OpenSettingsWindow();
    }

    private void OnDesktopGroupDeleteRequested(MenuCategory category)
    {
        TaskbarShortcutService.Remove(category);
        DesktopOrganizerService.RemoveCategory(_configuration!, category);
        _configurationStore!.Save(_configuration!);
        RefreshDesktopGroups();
    }

    void IDesktopGroupCommands.CreateGroup() => CreateDesktopGroup();

    /// <summary>
    /// Asks for a name next to the pointer and opens the new group right there, rather
    /// than in the top-left corner of the primary monitor — on a large screen that corner
    /// can be a long way from where the user was working.
    /// </summary>
    private MenuCategory? CreateDesktopGroup()
    {
        var cursor = System.Windows.Forms.Cursor.Position;
        var prompt = new TextPromptWindow(LocalizationService.Get("group.namePrompt"), string.Empty, cursor);
        if (prompt.ShowDialog() != true)
        {
            return null;
        }

        var group = new MenuCategory { Name = prompt.Value, IsDesktopGroup = true, Id = Guid.NewGuid().ToString("N") };
        _configuration!.GroupDefaults.ApplyTo(group);

        var position = NewGroupPosition(cursor, group.DesktopWidth, group.DesktopHeight);
        group.DesktopX = position.X;
        group.DesktopY = position.Y;

        _configuration.Categories.Add(group);
        _configurationStore!.Save(_configuration);
        RefreshDesktopGroups();
        return group;
    }

    /// <summary>Top-left, in DIPs, that puts a group of this size just under the pointer and fully on its monitor.</summary>
    private static System.Windows.Point NewGroupPosition(System.Drawing.Point cursor, double width, double height)
    {
        // Group windows are positioned in system-DPI units (the app is system DPI aware).
        var scale = PromptPositioning.GetSystemDpiScale();
        var area = Screen.FromPoint(cursor).WorkingArea;
        var workArea = new Rect(area.Left / scale, area.Top / scale, area.Width / scale, area.Height / scale);
        return PromptPositioning.CalculateNearCursorPosition(
            workArea,
            new System.Windows.Point(cursor.X / scale, cursor.Y / scale),
            width,
            height);
    }

    private void ShowTrayMenu()
    {
        var screenPoint = System.Windows.Forms.Cursor.Position;

        _menuHost!.Left = screenPoint.X;
        _menuHost.Top = screenPoint.Y;

        var menu = TrayMenuBuilder.Build(
            _configuration!,
            StartupRegistration.IsEnabled(),
            _desktopOrganizer!.HasOpenGroups,
            OpenSettingsWindow,
            () => CreateDesktopGroup(),
            _desktopOrganizer.ToggleCollapseAll,
            _desktopOrganizer.GatherAll,
            () =>
            {
                _desktopOrganizer.OpenAllGroups(_configuration!);
                SaveAndReloadGroups();
            },
            () =>
            {
                _desktopOrganizer.CloseAllGroups(_configuration!);
                SaveAndReloadGroups();
            },
            group =>
            {
                _desktopOrganizer.ToggleGroup(group);
                SaveAndReloadGroups();
            },
            StartupRegistration.SetEnabled,
            Shutdown);

        var cursorPosition = ToDeviceIndependentPoint(screenPoint);

        menu.PlacementTarget = _menuHost;
        menu.Placement = PlacementMode.AbsolutePoint;
        menu.HorizontalOffset = cursorPosition.X;
        menu.VerticalOffset = cursorPosition.Y;
        menu.IsOpen = true;
    }

    private System.Windows.Point ToDeviceIndependentPoint(System.Drawing.Point screenPoint)
    {
        var transform = PresentationSource.FromVisual(_menuHost!)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return transform.Transform(new System.Windows.Point(screenPoint.X, screenPoint.Y));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private static void BringWindowToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();

        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
        {
            SetForegroundWindow(hwnd);
        }
    }

    private void OpenSettingsWindow()
    {
        if (_settingsWindow is not null)
        {
            BringWindowToFront(_settingsWindow);
            return;
        }

        _settingsWindow = new SettingsWindow(_configuration!);
        _settingsWindow.ConfigurationSaved += OnConfigurationSaved;
        _settingsWindow.ImportShortcutsRequested += OnImportShortcutsRequested;
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow.ConfigurationSaved -= OnConfigurationSaved;
            _settingsWindow.ImportShortcutsRequested -= OnImportShortcutsRequested;
            _settingsWindow = null;
        };
        _settingsWindow.Show();
        BringWindowToFront(_settingsWindow);
    }

    /// <summary>Adds shortcuts picked in the settings window to a group, opening (or creating) it first.</summary>
    private void OnImportShortcutsRequested(string[] paths, MenuCategory? target, string newGroupName)
    {
        if (target is null)
        {
            target = new MenuCategory { Name = newGroupName, IsDesktopGroup = true, Id = Guid.NewGuid().ToString("N") };
            _configuration!.GroupDefaults.ApplyTo(target);
            var position = NewGroupPosition(System.Windows.Forms.Cursor.Position, target.DesktopWidth, target.DesktopHeight);
            target.DesktopX = position.X;
            target.DesktopY = position.Y;
            _configuration.Categories.Add(target);
        }

        target.IsClosed = false;
        _configurationStore!.Save(_configuration!);
        RefreshDesktopGroups();

        var added = _desktopOrganizer!.WindowFor(target)?.AddIncomingPaths(paths).Count ?? 0;
        _desktopOrganizer.FocusOpenGroup(target);
        _trayIcon?.ShowBalloonTip(
            3000,
            LocalizationService.Get("common.appName"),
            LocalizationService.Format("settings.importDone", added, target.Name),
            ToolTipIcon.Info);
    }

    private void OnConfigurationSaved(object? sender, EventArgs e)
    {
        // An imported configuration can bring shortcut files from another machine or an
        // older version: make them independent and the names unique, as at startup.
        ShortcutStore.AdoptAndRecoverAll(_configuration!);
        GroupNames.EnsureUnique(_configuration!);
        DesktopOrganizerService.EnsureIds(_configuration!);
        _configurationStore!.Save(_configuration!);
        _hotkeyService!.Apply(_configuration!.Behavior);
        RefreshDesktopGroups();
        _desktopOrganizer!.ReloadVisuals(_configuration!);
        AppThemeService.Apply(_configuration!.Behavior.AppTheme);
        LocalizationService.SetLanguage(
            string.IsNullOrEmpty(_configuration!.Behavior.Language)
                ? LocalizationService.DetectLanguage()
                : _configuration.Behavior.Language);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _iconCache?.Dispose();
        _hotkeyService?.Dispose();
        _desktopOrganizer?.StopWatchingDisplays();
        _desktopOrganizer?.CloseAll();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
