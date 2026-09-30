using System.Windows;
using SmartDockGroups.App.Theming;

namespace SmartDockGroups.App.Settings;

public partial class TextPromptWindow : ModernWindow
{
    private readonly System.Drawing.Point? _cursorOverride;
    private readonly Func<string, string?>? _validate;
    public string Value => ValueBox.Text.Trim();

    /// <param name="validate">
    /// Returns why a value cannot be accepted (shown under the box, the dialog stays
    /// open), or null to accept it. Used to refuse a name already taken in the group.
    /// </param>
    public TextPromptWindow(string prompt, string initialValue, System.Drawing.Point? cursorOverride = null, Func<string, string?>? validate = null)
    {
        _cursorOverride = cursorOverride;
        _validate = validate;
        InitializeComponent();
        ValueBox.TextChanged += (_, _) => ErrorText.Visibility = Visibility.Collapsed;

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

        if (_validate?.Invoke(Value) is { } problem)
        {
            ErrorText.Text = problem;
            ErrorText.Visibility = Visibility.Visible;
            ValueBox.SelectAll();
            ValueBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
