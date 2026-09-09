namespace SmartDockGroups.Core.Models;

public interface IMenuContainer
{
    List<MenuCategory> Categories { get; }
    List<LaunchItem> Items { get; }
}
