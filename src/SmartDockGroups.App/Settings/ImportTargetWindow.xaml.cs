using System.Windows;
using SmartDockGroups.App.Localization;
using SmartDockGroups.App.Theming;
using SmartDockGroups.Core.Models;

namespace SmartDockGroups.App.Settings;

/// <summary>
/// Asks which group a batch of imported shortcuts goes into: one of the existing groups,
/// or a new one named right here.
/// </summary>
public partial class ImportTargetWindow : ModernWindow
{
    private sealed record Option(string Label, MenuCategory? Group);

    public ImportTargetWindow(int count, IEnumerable<MenuCategory> groups)
    {
        InitializeComponent();

        PromptText.Text = LocalizationService.Format("settings.importTargetPrompt", count);

        var options = groups.Select(group => new Option(group.Name, group)).ToList();
        options.Add(new Option(LocalizationService.Get("settings.importNewGroup"), null));
        GroupBox.ItemsSource = options;
        GroupBox.SelectedIndex = 0;
        GroupBox.SelectionChanged += (_, _) =>
            NewGroupPanel.Visibility = SelectedOption?.Group is null ? Visibility.Visible : Visibility.Collapsed;
        NewGroupPanel.Visibility = SelectedOption?.Group is null ? Visibility.Visible : Visibility.Collapsed;

        Loaded += (_, _) => PromptPositioning.PositionWindowAtCursor(this);
    }

    private Option? SelectedOption => GroupBox.SelectedItem as Option;

    /// <summary>The existing group chosen, or null when <see cref="NewGroupName"/> should be created.</summary>
    public MenuCategory? TargetGroup => SelectedOption?.Group;

    public string NewGroupName => NewGroupNameBox.Text.Trim();

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (TargetGroup is null && string.IsNullOrWhiteSpace(NewGroupNameBox.Text))
        {
            NewGroupNameBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
