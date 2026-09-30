using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SmartDockGroups.App.Localization;
using SmartDockGroups.App.Theming;
using SmartDockGroups.Core.Configuration;
using SmartDockGroups.Core.Models;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace SmartDockGroups.App.Settings;

public partial class SettingsWindow : ModernWindow
{
    private readonly LauncherConfiguration _target;
    private readonly LauncherConfiguration _workingConfiguration;

    public event EventHandler? ConfigurationSaved;

    /// <summary>Shortcut files picked here, to be added to the chosen group (or to a new group with the given name).</summary>
    public event Action<string[], MenuCategory?, string>? ImportShortcutsRequested;

    /// <summary>
    /// Set once a configuration file has been imported. Only then does "Save" replace the
    /// groups; otherwise it applies the settings on this window and nothing else. Replacing
    /// them every time used to undo whatever changed in the groups (a move, a paste) while
    /// this window was open, because the copy it held was taken when it opened.
    /// </summary>
    private bool _configurationImported;

    public SettingsWindow(LauncherConfiguration configuration)
    {
        InitializeComponent();

        _target = configuration;
        _workingConfiguration = configuration.Clone();

        LoadBehavior();
    }

    private void LoadBehavior()
    {
        AppThemeBox.ItemsSource = new[]
        {
            new { Mode = AppThemeMode.System, Label = LocalizationService.Get("settings.themeSystem") },
            new { Mode = AppThemeMode.Light, Label = LocalizationService.Get("settings.themeLight") },
            new { Mode = AppThemeMode.Dark, Label = LocalizationService.Get("settings.themeDark") }
        };
        AppThemeBox.SelectedValue = _workingConfiguration.Behavior.AppTheme;

        var languageOptions = new[] { new { Code = (string?)null, Label = LocalizationService.Get("settings.languageAuto") } }
            .Concat(LocalizationService.SupportedLanguages.Select(l => new { Code = (string?)l.Code, Label = l.DisplayName }))
            .ToList();
        LanguageBox.ItemsSource = languageOptions;
        LanguageBox.SelectedItem = languageOptions.FirstOrDefault(l => l.Code == _workingConfiguration.Behavior.Language)
            ?? languageOptions[0];

        ClickModeBox.ItemsSource = new[]
        {
            new { Mode = TrayClickMode.SingleClick, Label = LocalizationService.Get("settings.singleClick") },
            new { Mode = TrayClickMode.DoubleClick, Label = LocalizationService.Get("settings.doubleClick") }
        };
        ClickModeBox.SelectedValue = _workingConfiguration.Behavior.ClickMode;

        var hotkeyKeys = new[] { "Space", "Tab" }
            .Concat(Enumerable.Range('A', 26).Select(code => ((char)code).ToString()))
            .ToList();

        HotkeyKeyBox.ItemsSource = hotkeyKeys;
        HotkeyKeyBox.SelectedItem = _workingConfiguration.Behavior.GlobalHotkeyKey;

        var modifiers = _workingConfiguration.Behavior.GlobalHotkeyModifiers;
        HotkeyEnabledBox.IsChecked = _workingConfiguration.Behavior.GlobalHotkeyEnabled;
        HotkeyCtrlBox.IsChecked = modifiers.HasFlag(HotkeyModifiers.Control);
        HotkeyAltBox.IsChecked = modifiers.HasFlag(HotkeyModifiers.Alt);
        HotkeyShiftBox.IsChecked = modifiers.HasFlag(HotkeyModifiers.Shift);
        HotkeyWinBox.IsChecked = modifiers.HasFlag(HotkeyModifiers.Windows);

        RestoreHotkeyKeyBox.ItemsSource = hotkeyKeys;
        RestoreHotkeyKeyBox.SelectedItem = _workingConfiguration.Behavior.RestoreGroupsHotkeyKey;

        var restoreModifiers = _workingConfiguration.Behavior.RestoreGroupsHotkeyModifiers;
        RestoreHotkeyEnabledBox.IsChecked = _workingConfiguration.Behavior.RestoreGroupsHotkeyEnabled;
        RestoreHotkeyCtrlBox.IsChecked = restoreModifiers.HasFlag(HotkeyModifiers.Control);
        RestoreHotkeyAltBox.IsChecked = restoreModifiers.HasFlag(HotkeyModifiers.Alt);
        RestoreHotkeyShiftBox.IsChecked = restoreModifiers.HasFlag(HotkeyModifiers.Shift);
        RestoreHotkeyWinBox.IsChecked = restoreModifiers.HasFlag(HotkeyModifiers.Windows);
    }

    private void SaveBehavior()
    {
        var behavior = _workingConfiguration.Behavior;
        behavior.AppTheme = AppThemeBox.SelectedValue as AppThemeMode? ?? AppThemeMode.System;
        behavior.Language = LanguageBox.SelectedValue as string;
        behavior.ClickMode = ClickModeBox.SelectedValue as TrayClickMode? ?? TrayClickMode.SingleClick;
        behavior.GlobalHotkeyEnabled = HotkeyEnabledBox.IsChecked == true;
        behavior.GlobalHotkeyKey = HotkeyKeyBox.SelectedItem as string ?? "Space";

        var modifiers = HotkeyModifiers.None;
        if (HotkeyCtrlBox.IsChecked == true)
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (HotkeyAltBox.IsChecked == true)
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (HotkeyShiftBox.IsChecked == true)
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (HotkeyWinBox.IsChecked == true)
        {
            modifiers |= HotkeyModifiers.Windows;
        }

        behavior.GlobalHotkeyModifiers = modifiers;

        behavior.RestoreGroupsHotkeyEnabled = RestoreHotkeyEnabledBox.IsChecked == true;
        behavior.RestoreGroupsHotkeyKey = RestoreHotkeyKeyBox.SelectedItem as string ?? "D";

        var restoreModifiers = HotkeyModifiers.None;
        if (RestoreHotkeyCtrlBox.IsChecked == true)
        {
            restoreModifiers |= HotkeyModifiers.Control;
        }

        if (RestoreHotkeyAltBox.IsChecked == true)
        {
            restoreModifiers |= HotkeyModifiers.Alt;
        }

        if (RestoreHotkeyShiftBox.IsChecked == true)
        {
            restoreModifiers |= HotkeyModifiers.Shift;
        }

        if (RestoreHotkeyWinBox.IsChecked == true)
        {
            restoreModifiers |= HotkeyModifiers.Windows;
        }

        behavior.RestoreGroupsHotkeyModifiers = restoreModifiers;
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = LocalizationService.Get("settings.jsonFilter"),
            FileName = "smartdockgroups-config.json"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        SaveBehavior();

        var export = _configurationImported ? _workingConfiguration.Clone() : _target.Clone();
        export.Behavior = _workingConfiguration.Behavior.Clone();
        new ConfigurationStore(dialog.FileName).Save(export);
    }

    /// <summary>
    /// Several shortcuts in one go: pick them all in the file dialog (a whole folder with
    /// Ctrl+A), then say which group receives them.
    /// </summary>
    private void OnImportShortcutsClick(object sender, RoutedEventArgs e)
    {
        var paths = Desktop.DesktopGroupWindow.PickShortcutFiles(this);
        if (paths.Length == 0)
        {
            return;
        }

        var picker = new ImportTargetWindow(paths.Length, Desktop.DesktopOrganizerService.AllDesktopGroups(_target)) { Owner = this };
        if (picker.ShowDialog() != true)
        {
            return;
        }

        ImportShortcutsRequested?.Invoke(paths, picker.TargetGroup, picker.NewGroupName);
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = LocalizationService.Get("settings.jsonFilter") };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (!new ConfigurationStore(dialog.FileName).TryLoad(out var imported))
        {
            MessageBox.Show(
                this,
                LocalizationService.Get("settings.importErrorMessage"),
                LocalizationService.Get("common.appName"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        GroupNames.EnsureUnique(imported);
        _workingConfiguration.ReplaceContentsWith(imported);
        _configurationImported = true;
        LoadBehavior();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        SaveBehavior();
        if (_configurationImported)
        {
            _target.ReplaceContentsWith(_workingConfiguration);
            _configurationImported = false;
        }
        else
        {
            _target.Behavior = _workingConfiguration.Behavior.Clone();
        }

        ConfigurationSaved?.Invoke(this, EventArgs.Empty);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
