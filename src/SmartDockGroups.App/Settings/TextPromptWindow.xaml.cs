using System.Windows;
using SmartDockGroups.App.Theming;

namespace SmartDockGroups.App.Settings;

public partial class TextPromptWindow : ModernWindow
{
    private readonly System.Drawing.Point? _cursorOverride;
    public string Value => ValueBox.Text.Trim();

    public TextPromptWindow(string prompt, string initialValue, System.Drawing.Point? cursorOverride = null)
    {
        _cursorOverride = cursorOverride;
        InitializeComponent();

        PromptText.Text = prompt;
        ValueBox.Text = initialValue;
        ValueBox.SelectAll();
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            PromptPositioning.PositionWindowAtCursor(this, _cursorOverride);
            Topmost = true;
            Topmost = false;
            Activate();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        PromptPositioning.PositionWindowAtCursor(this, _cursorOverride);
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ValueBox.Text))
        {
            return;
        }

        DialogResult = true;
    }
}
