using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SmartDockGroups.Probe;

/// <summary>
/// The Win32 surface the probe needs. Written in C# on purpose: PowerShell's marshalling of
/// <c>$null</c> into a P/Invoke <c>string</c> parameter has already produced two wrong
/// conclusions in this project (see docs/OVERVIEW.md, "Nota sobre PowerShell"), so every
/// window diagnostic here goes through real interop instead.
/// </summary>
internal static class Win32
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool GetCursorPos(out Point lpPoint);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern short VkKeyScan(char ch);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern IntPtr GetThreadDesktop(int dwThreadId);
    [DllImport("kernel32.dll")] public static extern int GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true)] public static extern bool GetUserObjectInformation(IntPtr hObj, int nIndex, StringBuilder pvInfo, int nLength, out int lpnLengthNeeded);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CreateProcess(
        string? lpApplicationName, string? lpCommandLine,
        IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
        bool bInheritHandles, uint dwCreationFlags,
        IntPtr lpEnvironment, string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GetStdHandle(int nStdHandle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    public static string GetCurrentDesktopName()
    {
        try
        {
            var hDesk = GetThreadDesktop(GetCurrentThreadId());
            var name = new StringBuilder(256);
            if (GetUserObjectInformation(hDesk, 2 /* UOI_NAME */, name, name.Capacity, out _))
            {
                return name.ToString();
            }
        }
        catch
        {
        }
        return string.Empty;
    }

    public static int RelaunchOnDefaultDesktop(string[] args)
    {
        var exePath = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];
        var escapedArgs = string.Join(" ", args.Concat(new[] { "--no-desktop-relaunch" }).Select(a => $"\"{a}\""));
        var cmdLine = $"\"{exePath}\" {escapedArgs}";

        var si = new STARTUPINFO();
        si.cb = Marshal.SizeOf(si);
        si.lpDesktop = @"WinSta0\Default";
        si.dwFlags = 0x00000100; // STARTF_USESTDHANDLES
        si.hStdInput = GetStdHandle(-10); // STD_INPUT_HANDLE
        si.hStdOutput = GetStdHandle(-11); // STD_OUTPUT_HANDLE
        si.hStdError = GetStdHandle(-12); // STD_ERROR_HANDLE

        if (!CreateProcess(null, cmdLine, IntPtr.Zero, IntPtr.Zero, true, 0, IntPtr.Zero, null, ref si, out var pi))
        {
            Console.Error.WriteLine($"Failed to relaunch on WinSta0\\Default: error {Marshal.GetLastWin32Error()}");
            return 1;
        }

        WaitForSingleObject(pi.hProcess, 0xFFFFFFFF);
        GetExitCodeProcess(pi.hProcess, out var exitCode);
        return (int)exitCode;
    }

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_APPWINDOW = 0x00040000;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TRANSPARENT = 0x00000020;

    public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;

    public const byte VK_ESCAPE = 0x1B;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    public const uint PW_RENDERFULLCONTENT = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    public static string ClassOf(IntPtr hWnd)
    {
        var buffer = new StringBuilder(256);
        GetClassName(hWnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    public static string TitleOf(IntPtr hWnd)
    {
        var buffer = new StringBuilder(512);
        GetWindowText(hWnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    public static List<IntPtr> VisibleWindowsOf(uint processId)
    {
        var found = new List<IntPtr>();
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var owner);
            if (owner == processId && IsWindowVisible(hWnd))
            {
                found.Add(hWnd);
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    public static void RightClick(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(220);
        mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
    }

    public static void LeftClick(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(100);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
    }

    public static void CaptureRect(Rectangle rect, string pngPath)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            throw new ArgumentException($"Invalid rectangle dimensions for capture: {rect}");
        }

        using var bitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, rect.Size);
        }

        var dir = Path.GetDirectoryName(pngPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        bitmap.Save(pngPath, ImageFormat.Png);
    }

    public static void PressEscape()
    {
        keybd_event(VK_ESCAPE, 0, 0, UIntPtr.Zero);
        keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    /// <summary>
    /// Captures the window's own pixels. <c>PW_RENDERFULLCONTENT</c> is what makes this work for
    /// the group windows, which are <c>AllowsTransparency</c> (layered) — a plain BitBlt of the
    /// screen would capture whatever sits behind them instead.
    /// </summary>
    public static Bitmap? CaptureWindowBitmap(IntPtr hWnd)
    {
        if (!GetWindowRect(hWnd, out var rect) || rect.Width <= 0 || rect.Height <= 0)
        {
            return null;
        }

        var bitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            var hdc = graphics.GetHdc();
            try
            {
                if (!PrintWindow(hWnd, hdc, PW_RENDERFULLCONTENT))
                {
                    bitmap.Dispose();
                    return null;
                }
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }
        return bitmap;
    }

    public static bool TryCapture(IntPtr hWnd, string pngPath)
    {
        using var bmp = CaptureWindowBitmap(hWnd);
        if (bmp == null)
        {
            return false;
        }

        var dir = Path.GetDirectoryName(pngPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        bmp.Save(pngPath, ImageFormat.Png);
        return true;
    }

    public static void CaptureWindowsComposite(IntPtr groupHwnd, IntPtr dialogHwnd, string pngPath)
    {
        GetWindowRect(groupHwnd, out var grpRect);
        GetWindowRect(dialogHwnd, out var dlgRect);

        const int margin = 16;
        int left = Math.Min(grpRect.Left, dlgRect.Left) - margin;
        int top = Math.Min(grpRect.Top, dlgRect.Top) - margin;
        int right = Math.Max(grpRect.Right, dlgRect.Right) + margin;
        int bottom = Math.Max(grpRect.Bottom, dlgRect.Bottom) + margin;

        int width = Math.Max(1, right - left);
        int height = Math.Max(1, bottom - top);

        using var composite = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(composite))
        {
            // Fundo escuro limpo (#1E1E1E - cor de fundo padrão do tema escuro do app)
            g.Clear(Color.FromArgb(0x1E, 0x1E, 0x1E));

            using var grpBmp = CaptureWindowBitmap(groupHwnd);
            if (grpBmp != null)
            {
                g.DrawImage(grpBmp, grpRect.Left - left, grpRect.Top - top, grpRect.Width, grpRect.Height);
            }

            using var dlgBmp = CaptureWindowBitmap(dialogHwnd);
            if (dlgBmp != null)
            {
                g.DrawImage(dlgBmp, dlgRect.Left - left, dlgRect.Top - top, dlgRect.Width, dlgRect.Height);
            }
        }

        var dir = Path.GetDirectoryName(pngPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        composite.Save(pngPath, ImageFormat.Png);
    }
}
