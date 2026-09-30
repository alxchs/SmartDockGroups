using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SmartDockGroups.Core.Models;

namespace SmartDockGroups.App.Services;

/// <summary>
/// Writes a <c>.lnk</c> that brings one desktop group to the front: it launches this same
/// executable with <c>--desktop-action=focus-group:&lt;id&gt;</c>, and the already-running instance
/// picks the action up through <see cref="SingleInstanceCoordinator"/>.
///
/// Where it goes: the user's Start menu, in a "Smart Dock Groups" folder
/// (<see cref="ApplicationPaths.GroupShortcutsDirectory"/>, from the Programs known folder — never a
/// hard-coded path). From there the group is found by typing its name in Start, and pinned with
/// "Pin to taskbar" on the Start result or on the file itself.
///
/// Why the app does not pin it: measured on 2026-09-30 (Windows 11 26H2), (1) a <c>.lnk</c> in
/// <c>Quick Launch\User Pinned\TaskBar</c> is not a pinned button — Explorer draws the taskbar from
/// the binary <c>Taskband\Favorites</c> value, and that folder only holds the files it refers to (a
/// "Google Chrome.lnk" sat there unpinned); (2) the shell's own pin interface (IPinnedList3.Modify)
/// answers S_OK to any process other than Explorer and changes nothing. Pinning is reserved for the
/// user, and working around that would mean impersonating Explorer or writing an undocumented blob.
///
/// Every group shortcut carries its own AppUserModelID. Without it the taskbar sees every group
/// shortcut as "SmartDockGroups.App.exe" — pin one and the next one already reads "Unpin".
/// </summary>
internal static class TaskbarShortcutService
{
    private const string AppUserModelIdPrefix = "SmartDockGroups.Group.";

    /// <summary>Creates (or refreshes) the group's shortcut and returns its path.</summary>
    public static string CreateGroupShortcut(MenuCategory group, string executablePath)
    {
        if (string.IsNullOrEmpty(group.Id))
        {
            throw new InvalidOperationException("The group has no Id yet.");
        }

        Directory.CreateDirectory(ApplicationPaths.GroupShortcutsDirectory);

        // One shortcut per group: a rename replaces the old file rather than leaving it behind.
        var existing = FindShortcut(group.Id);
        var path = PathFor(group);
        if (existing is not null && !string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(existing);
        }

        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(executablePath);
            link.SetArguments(DesktopContextMenuRegistration.FocusGroupArguments(group.Id));
            link.SetWorkingDirectory(Path.GetDirectoryName(executablePath) ?? string.Empty);
            link.SetIconLocation(executablePath, 0);
            link.SetDescription(group.Name);

            var store = (IPropertyStore)link;
            var key = AppUserModelIdKey;
            var value = PropVariant.FromString(AppUserModelIdPrefix + group.Id);
            try
            {
                store.SetValue(ref key, ref value);
                store.Commit();
            }
            finally
            {
                value.Clear();
            }

            ((IPersistFile)link).Save(path, true);
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }

        return path;
    }

    /// <summary>
    /// Keeps an existing shortcut's file name and description in step with the group's name.
    /// Does nothing when the group never had a shortcut — renaming must not create one.
    /// </summary>
    public static void SyncName(MenuCategory group, string executablePath)
    {
        if (string.IsNullOrEmpty(group.Id) || FindShortcut(group.Id) is not { } existing)
        {
            return;
        }

        if (!string.Equals(existing, PathFor(group), StringComparison.OrdinalIgnoreCase))
        {
            CreateGroupShortcut(group, executablePath);
        }
    }

    /// <summary>Deletes the shortcut of a group that no longer exists, so Start does not list it.</summary>
    public static void Remove(MenuCategory group)
    {
        if (!string.IsNullOrEmpty(group.Id) && FindShortcut(group.Id) is { } existing)
        {
            TryDelete(existing);
        }
    }

    /// <summary>Opens Explorer on the folder with the shortcut selected, ready for "Pin to taskbar".</summary>
    public static void RevealInExplorer(string shortcutPath)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{shortcutPath}\"") { UseShellExecute = true });
    }

    /// <summary>The shortcut in the folder that opens this group — found by id, so it survives renames.</summary>
    internal static string? FindShortcut(string groupId)
    {
        if (!Directory.Exists(ApplicationPaths.GroupShortcutsDirectory))
        {
            return null;
        }

        var arguments = DesktopContextMenuRegistration.FocusGroupArguments(groupId);
        foreach (var file in Directory.EnumerateFiles(ApplicationPaths.GroupShortcutsDirectory, "*.lnk"))
        {
            if (string.Equals(ReadArguments(file), arguments, StringComparison.Ordinal))
            {
                return file;
            }
        }

        return null;
    }

    /// <summary>
    /// "&lt;name&gt;.lnk" — what the user types in Start — unless another group's shortcut already
    /// holds that name, in which case the short id keeps the two apart.
    /// </summary>
    private static string PathFor(MenuCategory group)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = string.Concat(group.Name.Select(c => invalid.Contains(c) ? '_' : c)).Trim().TrimEnd('.');
        if (name.Length == 0)
        {
            name = "Group";
        }

        var plain = Path.Combine(ApplicationPaths.GroupShortcutsDirectory, name + ".lnk");
        var arguments = DesktopContextMenuRegistration.FocusGroupArguments(group.Id!);
        return !File.Exists(plain) || string.Equals(ReadArguments(plain), arguments, StringComparison.Ordinal)
            ? plain
            : Path.Combine(ApplicationPaths.GroupShortcutsDirectory, $"{name} ({group.Id![..6]}).lnk");
    }

    private static string? ReadArguments(string shortcutPath)
    {
        try
        {
            var link = (IShellLinkW)new ShellLink();
            try
            {
                ((IPersistFile)link).Load(shortcutPath, 0);
                var arguments = new StringBuilder(2048);
                link.GetArguments(arguments, arguments.Capacity);
                return arguments.ToString();
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

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[TaskbarShortcut] could not delete {path}: {ex.Message}");
        }
    }

    /// <summary>PKEY_AppUserModel_ID.</summary>
    private static PropertyKey AppUserModelIdKey => new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    /// <summary>A PROPVARIANT holding a VT_LPWSTR — the only kind this class writes.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] private ushort _type;
        [FieldOffset(8)] private IntPtr _pointer;

        public static PropVariant FromString(string value) => new()
        {
            _type = 31, // VT_LPWSTR
            _pointer = Marshal.StringToCoTaskMemUni(value)
        };

        public void Clear() => PropVariantClear(ref this);

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant value);
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

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }
}
