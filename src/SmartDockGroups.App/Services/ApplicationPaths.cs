using System.IO;

namespace SmartDockGroups.App.Services;

internal static class ApplicationPaths
{
    private const string ProductFolderName = "SmartDockGroups";

    /// <summary>
    /// Points the whole data folder somewhere else, for testing against a throwaway
    /// configuration. Such an instance is also isolated (<see cref="InstanceSuffix"/>) and
    /// leaves the desktop context menu registration alone, so it can run beside the real
    /// app without touching the user's groups, registry or single-instance lock.
    /// </summary>
    private const string DataFolderOverrideVariable = "SMARTDOCKGROUPS_DATA_DIR";

    private static readonly string? DataFolderOverride =
        Environment.GetEnvironmentVariable(DataFolderOverrideVariable) is { Length: > 0 } value ? Path.GetFullPath(value) : null;

    public static bool IsIsolatedInstance => DataFolderOverride is not null;

    /// <summary>Appended to the mutex and pipe names of an isolated instance; empty for the real one.</summary>
    public static string InstanceSuffix => DataFolderOverride is null
        ? string.Empty
        : "." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(DataFolderOverride.ToLowerInvariant())))[..12];

    private static string ProductDataFolder { get; } = DataFolderOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ProductFolderName);

    public static string ConfigFilePath { get; } = Path.Combine(ProductDataFolder, "config.json");
    public static string IconCacheDirectory { get; } = Path.Combine(ProductDataFolder, "IconCache");
    public static string GroupShortcutsDirectory { get; } = Path.Combine(ProductDataFolder, "GroupShortcuts");

    /// <summary>The app's own copies of the .lnk/.url files placed in groups. See <see cref="ShortcutStore"/>.</summary>
    public static string OwnedShortcutsDirectory { get; } = Path.Combine(ProductDataFolder, "Shortcuts");
}
