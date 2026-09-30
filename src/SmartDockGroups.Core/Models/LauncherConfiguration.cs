namespace SmartDockGroups.Core.Models;

public sealed class LauncherConfiguration : IMenuContainer
{
    public List<MenuCategory> Categories { get; init; } = [];
    public List<LaunchItem> Items { get; init; } = [];
    public MenuTheme Theme { get; set; } = new();
    public LauncherBehavior Behavior { get; set; } = new();

    /// <summary>The non-colour look new groups start with. See <see cref="Models.GroupDefaults"/>.</summary>
    public GroupDefaults GroupDefaults { get; set; } = new();

    public LauncherConfiguration Clone()
    {
        return new LauncherConfiguration
        {
            Items = [.. Items.Select(item => item.Clone())],
            Categories = [.. Categories.Select(category => category.Clone())],
            Theme = Theme.Clone(),
            Behavior = Behavior.Clone(),
            GroupDefaults = GroupDefaults.Clone()
        };
    }

    public void ReplaceContentsWith(LauncherConfiguration source)
    {
        Items.Clear();
        Items.AddRange(source.Items.Select(item => item.Clone()));
        Categories.Clear();
        Categories.AddRange(source.Categories.Select(category => category.Clone()));
        Theme = source.Theme.Clone();
        Behavior = source.Behavior.Clone();
        GroupDefaults = source.GroupDefaults.Clone();
    }

    public static LauncherConfiguration CreateDefault()
    {
        return new LauncherConfiguration
        {
            Items =
            [
                new LaunchItem
                {
                    Name = "Bloco de Notas",
                    Type = LaunchItemType.Application,
                    Target = "notepad.exe"
                }
            ],
            Categories =
            [
                new MenuCategory
                {
                    Name = "Ferramentas do Windows",
                    ThemeOverride = new MenuTheme
                    {
                        HighlightColor = "#3DFFA34D"
                    },
                    Items =
                    [
                        new LaunchItem
                        {
                            Name = "Calculadora",
                            Type = LaunchItemType.Application,
                            Target = "calc.exe"
                        },
                        new LaunchItem
                        {
                            Name = "Paint",
                            Type = LaunchItemType.Application,
                            Target = "mspaint.exe"
                        }
                    ]
                }
            ]
        };
    }
}
