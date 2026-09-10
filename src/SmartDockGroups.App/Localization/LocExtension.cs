using System.Windows.Markup;

namespace SmartDockGroups.App.Localization;

[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; }

    public LocExtension()
    {
        Key = string.Empty;
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return LocalizationService.Get(Key);
    }
}
