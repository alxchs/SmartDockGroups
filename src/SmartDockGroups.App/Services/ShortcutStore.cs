using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SmartDockGroups.Core.Models;

namespace SmartDockGroups.App.Services;

/// <summary>
/// Keeps a group's shortcut files alive on their own.
///
/// A group used to remember only the path of the <c>.lnk</c>/<c>.url</c> it was given.
/// Those files belong to somebody else: the taskbar deletes its pinned <c>.lnk</c> the
/// moment an app is unpinned, and a desktop gets tidied up — which is the whole point of
/// moving its icons into groups. Measured on 2026-09-30: all six items that "lost their
/// icon" (DBeaver, Parametrizacao, SSMS 22, Edge, NVIDIA App, WinDirStat) pointed at
/// <c>...\User Pinned\TaskBar\*.lnk</c> files that no longer existed.
///
/// So a shortcut file is <b>adopted</b> as soon as it joins a group: copied into
/// <see cref="ApplicationPaths.OwnedShortcutsDirectory"/>, the same thing Explorer does
/// when a shortcut is pasted into another folder. One already lost is <b>recovered</b>
/// from the places Windows keeps shortcuts (Start menu, desktops, taskbar) when a match
/// can be told apart without guessing.
/// </summary>
internal static class ShortcutStore
{
    private static readonly string[] ShortcutExtensions = [".lnk", ".url"];

    public static bool IsShortcutFile(string path) =>
        ShortcutExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    public static bool IsOwned(string path) =>
        Path.GetFullPath(path).StartsWith(
            Path.GetFullPath(ApplicationPaths.OwnedShortcutsDirectory) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The path a group should remember for <paramref name="path"/>: a private copy for a
    /// shortcut file, the path itself for anything else (a program, a document, a folder).
    /// </summary>
    public static string Adopt(string path)
    {
        if (!IsShortcutFile(path) || !File.Exists(path) || IsOwned(path))
        {
            return path;
        }

        try
        {
            Directory.CreateDirectory(ApplicationPaths.OwnedShortcutsDirectory);
            var name = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);
            var destination = Path.Combine(ApplicationPaths.OwnedShortcutsDirectory, name + extension);
            for (var counter = 2; File.Exists(destination); counter++)
            {
                // The same shortcut adopted twice (two groups) can share one copy.
                if (FilesEqual(destination, path))
                {
                    return destination;
                }

                destination = Path.Combine(ApplicationPaths.OwnedShortcutsDirectory, $"{name} ({counter}){extension}");
            }

            File.Copy(path, destination);
            return destination;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[ShortcutStore] Adopt failed for {path}: {ex.Message}");
            return path;
        }
    }

    /// <summary>
    /// Makes every shortcut in the configuration independent of its original file:
    /// adopts the ones still on disk and recovers the ones already gone. True when any
    /// item's target changed, so the caller knows to save.
    /// </summary>
    public static bool AdoptAndRecoverAll(IMenuContainer container, List<string>? unrecovered = null)
    {
        var changed = false;
        foreach (var group in container.Categories)
        {
            foreach (var item in group.Items)
            {
                changed |= AdoptOrRecover(item, unrecovered);
            }

            changed |= AdoptAndRecoverAll(group, unrecovered);
        }

        return changed;
    }

    private static bool AdoptOrRecover(LaunchItem item, List<string>? unrecovered)
    {
        var target = item.Target;
        if (string.IsNullOrWhiteSpace(target) || target.Contains("://") || !IsShortcutFile(target))
        {
            return false;
        }

        // Still reachable (possibly through the old unique-similar-name rule for a
        // renamed .url): take a copy of whatever it resolves to.
        var reachable = File.Exists(target) ? target : IconCacheService.ResolveFullPath(target);
        if (reachable is null)
        {
            reachable = FindReplacement(target, item.Name);
            if (reachable is null)
            {
                unrecovered?.Add(item.Name);
                return false;
            }
        }

        var adopted = Adopt(reachable);
        if (string.Equals(adopted, target, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        item.Target = adopted;
        return true;
    }

    /// <summary>The folders where Windows itself keeps shortcuts to installed programs.</summary>
    private static IEnumerable<string> SearchRoots()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Path.Combine(appData, "Microsoft", "Internet Explorer", "Quick Launch")
        }
        .Where(root => !string.IsNullOrEmpty(root) && Directory.Exists(root))
        .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A shortcut elsewhere that stands for the lost one. Tried in order, and the first
    /// rule that finds anything decides: the same file name; the item's display name; a
    /// name one is a prefix of ("DBeaver" for "DBeaver Community"). Several matches are
    /// accepted only when they all launch the same program — the Start menu often has a
    /// per-user and an all-users copy of one shortcut — and never otherwise.
    /// </summary>
    internal static string? FindReplacement(string lostPath, string itemName, IEnumerable<string>? roots = null)
    {
        var extension = Path.GetExtension(lostPath);
        var lostName = Path.GetFileNameWithoutExtension(lostPath);

        List<string> candidates;
        try
        {
            candidates = (roots ?? SearchRoots())
                .SelectMany(root => Directory.EnumerateFiles(root, "*" + extension, new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true
                }))
                .Where(path => !Path.GetFileNameWithoutExtension(path).StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        static bool NameIs(string path, string name) =>
            string.Equals(Path.GetFileNameWithoutExtension(path), name.Trim(), StringComparison.OrdinalIgnoreCase);

        static bool PrefixOf(string path, string name)
        {
            var candidate = Path.GetFileNameWithoutExtension(path);
            return candidate.Length >= 4 && name.Length >= 4
                && (name.StartsWith(candidate + " ", StringComparison.OrdinalIgnoreCase)
                    || candidate.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase));
        }

        var rules = new Func<string, bool>[]
        {
            path => NameIs(path, lostName),
            path => NameIs(path, itemName),
            path => PrefixOf(path, lostName) || PrefixOf(path, itemName)
        };

        foreach (var rule in rules)
        {
            var matches = candidates.Where(rule).ToList();
            if (matches.Count == 0)
            {
                continue;
            }

            if (matches.Count == 1)
            {
                return matches[0];
            }

            var targets = matches.Select(ReadLinkTarget).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return targets.Count == 1 && !string.IsNullOrEmpty(targets[0]) ? matches[0] : null;
        }

        return null;
    }

    /// <summary>What a <c>.lnk</c> launches (path plus arguments), or the URL of a <c>.url</c>.</summary>
    internal static string? ReadLinkTarget(string shortcutPath)
    {
        if (shortcutPath.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return File.ReadLines(shortcutPath)
                    .FirstOrDefault(line => line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))?[4..].Trim();
            }
            catch (IOException)
            {
                return null;
            }
        }

        try
        {
            var link = (IShellLinkW)new ShellLink();
            try
            {
                ((IPersistFile)link).Load(shortcutPath, 0);
                var path = new StringBuilder(1024);
                link.GetPath(path, path.Capacity, IntPtr.Zero, 0);
                var arguments = new StringBuilder(2048);
                link.GetArguments(arguments, arguments.Capacity);

                // An advertised (installer) shortcut has no plain path; it cannot be compared.
                return path.Length == 0 ? null : $"{path}|{arguments}";
            }
            finally
            {
                Marshal.ReleaseComObject(link);
            }
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>
    /// The program a <c>.lnk</c> takes its picture from, when the shortcut does not name an
    /// icon of its own (its icon location is empty — "use the target's icon"). Null when the
    /// shortcut names its own icon, is not a <c>.lnk</c>, or its target is not a file on disk.
    ///
    /// Why this exists: for some of these shortcuts the Windows shell hands back a blank
    /// document icon as soon as the process is DPI aware — every shell API tried
    /// (ExtractAssociatedIcon, SHGetFileInfo, the jumbo image list, IShellItemImageFactory)
    /// failed the same way on "SQL Server Management Studio 22.lnk" at 150% (2026-09-30),
    /// while the target SSMS.exe itself extracted correctly with each of them.
    /// </summary>
    internal static string? IconSourceForTargetIcon(string shortcutPath)
    {
        if (!shortcutPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || !File.Exists(shortcutPath))
        {
            return null;
        }

        try
        {
            var link = (IShellLinkW)new ShellLink();
            try
            {
                ((IPersistFile)link).Load(shortcutPath, 0);
                var iconPath = new StringBuilder(1024);
                link.GetIconLocation(iconPath, iconPath.Capacity, out _);
                if (iconPath.Length > 0)
                {
                    return null;
                }

                var target = new StringBuilder(1024);
                link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
                var resolved = Environment.ExpandEnvironmentVariables(target.ToString());
                return resolved.Length > 0 && File.Exists(resolved) ? resolved : null;
            }
            finally
            {
                Marshal.ReleaseComObject(link);
            }
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static bool FilesEqual(string a, string b)
    {
        try
        {
            return new FileInfo(a).Length == new FileInfo(b).Length
                && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
        }
        catch (IOException)
        {
            return false;
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFileName([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}
