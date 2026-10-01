namespace SmartDockGroups.Core.Models;

public enum LaunchItemType
{
    Application,
    File,
    Folder,
    Url,
    Command,

    /// <summary>A shortcut to another desktop group: <see cref="LaunchItem.Target"/> is that group's <c>Id</c>.</summary>
    GroupLink
}
