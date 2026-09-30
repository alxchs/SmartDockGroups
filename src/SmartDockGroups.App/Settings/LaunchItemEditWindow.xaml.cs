using System.Windows;
using SmartDockGroups.App.Localization;
using SmartDockGroups.App.Theming;
using SmartDockGroups.Core.Models;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using OpenFolderDialog = Microsoft.Win32.OpenFolderDialog;

namespace SmartDockGroups.App.Settings;

public partial class LaunchItemEditWindow : ModernWindow
{
    private readonly LaunchItem _item;
    private readonly Func<string, string?>? _validateName;

    /// <param name="validateName">Why a name cannot be used (already taken in the group), or null when it can.</param>
    public LaunchItemEditWindow(LaunchItem item, Func<string, string?>? validateName = null)
    {
        InitializeComponent();

        _item = item;
        _validateName = validateName;

        TypeBox.ItemsSource = Enum.GetValues<LaunchItemType>();
        ExecutionModeBox.ItemsSource = Enum.GetValues<ExecutionMode>();
        NameBox.Text = item.Name;
        TypeBox.SelectedItem = item.Type;
        TargetBox.Text = item.Target;
        ArgumentsBox.Text = item.Arguments;
        WorkingDirectoryBox.Text = item.WorkingDirectory;
        ExecutionModeBox.SelectedItem = item.ExecutionMode;
        IconOverrideBox.Text = item.IconOverridePath;
        DesktopPinnedBox.IsChecked = item.IsDesktopPinned;
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var selectedType = TypeBox.SelectedItem as LaunchItemType? ?? LaunchItemType.Application;

        if (selectedType == LaunchItemType.Folder)
        {
            var folderDialog = new OpenFolderDialog();
            if (folderDialog.ShowDialog(this) == true)
            {
                TargetBox.Text = folderDialog.FolderName;
            }

            return;
        }

        var fileDialog = new OpenFileDialog { Filter = LocalizationService.Get("item.allFilesFilter") };
        if (fileDialog.ShowDialog(this) == true)
        {
            TargetBox.Text = fileDialog.FileName;
        }
    }

    private void OnBrowseIconClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = LocalizationService.Get("item.iconFilter")
        };

        if (dialog.ShowDialog(this) == true)
        {
            IconOverrideBox.Text = dialog.FileName;
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(TargetBox.Text) || TypeBox.SelectedItem is null)
        {
            MessageBox.Show(
                this,
                LocalizationService.Get("item.validationError"),
                LocalizationService.Get("common.appName"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (_validateName?.Invoke(NameBox.Text.Trim()) is { } problem)
        {
            MessageBox.Show(this, problem, LocalizationService.Get("common.appName"), MessageBoxButton.OK, MessageBoxImage.Warning);
            NameBox.Focus();
            NameBox.SelectAll();
            return;
        }

        _item.Name = NameBox.Text.Trim();
        _item.Type = (LaunchItemType)TypeBox.SelectedItem;
        _item.Target = TargetBox.Text.Trim();
        _item.Arguments = string.IsNullOrWhiteSpace(ArgumentsBox.Text) ? null : ArgumentsBox.Text.Trim();
        _item.WorkingDirectory = string.IsNullOrWhiteSpace(WorkingDirectoryBox.Text) ? null : WorkingDirectoryBox.Text.Trim();
        _item.ExecutionMode = ExecutionModeBox.SelectedItem as ExecutionMode? ?? ExecutionMode.Normal;
        _item.IconOverridePath = string.IsNullOrWhiteSpace(IconOverrideBox.Text) ? null : IconOverrideBox.Text.Trim();
        _item.IsDesktopPinned = DesktopPinnedBox.IsChecked == true;

        DialogResult = true;
    }
}
