using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace SmartDockGroups.App.Settings;

/// <summary>
/// Helper for positioning modal prompt dialogs on the monitor containing the cursor,
/// converting physical screen coordinates to WPF device-independent units (DIPs)
/// and centering within that monitor's work area.
/// </summary>
internal static class PromptPositioning
{
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    public static double GetSystemDpiScale()
    {
        try
        {
            uint dpi = GetDpiForSystem();
            return dpi > 0 ? dpi / 96.0 : 1.0;
        }
        catch
        {
            return 1.0;
        }
    }

    public static double GetMonitorDpiScale(Screen screen)
    {
        try
        {
            var pt = new POINT { X = screen.Bounds.Left + 10, Y = screen.Bounds.Top + 10 };
            var hmon = MonitorFromPoint(pt, 2 /* MONITOR_DEFAULTTONEAREST */);
            if (hmon != IntPtr.Zero && GetDpiForMonitor(hmon, 0, out uint dpiX, out _) == 0 && dpiX > 0)
            {
                return dpiX / 96.0;
            }
        }
        catch
        {
        }

        return GetSystemDpiScale();
    }

    /// <summary>
    /// Calculates the top-left (Left, Top) coordinate to center a window of the given size
    /// inside a work area (all in DIPs), clamping to ensure the window never extends
    /// outside the work area.
    /// </summary>
    public static Point CalculateCenteredPosition(Rect workAreaDip, double windowWidthDip, double windowHeightDip)
    {
        double x = workAreaDip.Left + Math.Max(0, (workAreaDip.Width - windowWidthDip) / 2.0);
        double y = workAreaDip.Top + Math.Max(0, (workAreaDip.Height - windowHeightDip) / 2.0);

        if (windowWidthDip <= workAreaDip.Width)
        {
            if (x + windowWidthDip > workAreaDip.Right)
            {
                x = workAreaDip.Right - windowWidthDip;
            }

            if (x < workAreaDip.Left)
            {
                x = workAreaDip.Left;
            }
        }
        else
        {
            x = workAreaDip.Left;
        }

        if (windowHeightDip <= workAreaDip.Height)
        {
            if (y + windowHeightDip > workAreaDip.Bottom)
            {
                y = workAreaDip.Bottom - windowHeightDip;
            }

            if (y < workAreaDip.Top)
            {
                y = workAreaDip.Top;
            }
        }
        else
        {
            y = workAreaDip.Top;
        }

        return new Point(x, y);
    }

    /// <summary>
    /// Converts a physical pixel rectangle (e.g. Screen.WorkingArea) to WPF DIPs using the
    /// CompositionTarget.TransformFromDevice matrix.
    /// </summary>
    public static Rect PhysicalToDip(System.Drawing.Rectangle physicalRect, Matrix transformFromDevice)
    {
        var topLeft = transformFromDevice.Transform(new Point(physicalRect.Left, physicalRect.Top));
        var bottomRight = transformFromDevice.Transform(new Point(physicalRect.Right, physicalRect.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    /// <summary>
    /// Converts a physical pixel rectangle to WPF DIPs using explicit DPI scales.
    /// </summary>
    public static Rect PhysicalToDip(System.Drawing.Rectangle physicalRect, double dpiScaleX, double dpiScaleY)
    {
        double scaleX = dpiScaleX > 0 ? dpiScaleX : 1.0;
        double scaleY = dpiScaleY > 0 ? dpiScaleY : 1.0;
        return new Rect(
            physicalRect.Left / scaleX,
            physicalRect.Top / scaleY,
            physicalRect.Width / scaleX,
            physicalRect.Height / scaleY);
    }

    /// <summary>
    /// Top-left for a window that should open where the user is looking: centred
    /// horizontally on the pointer and just below it, then pushed back inside the work
    /// area when it would spill over an edge. On a very large monitor the middle of the
    /// screen can be a long way from where the click happened; this keeps the prompt
    /// under the hand that asked for it. Units are whatever the arguments are in.
    /// </summary>
    public static Point CalculateNearCursorPosition(Rect workArea, Point cursor, double windowWidth, double windowHeight, double gap = 12)
    {
        var x = cursor.X - (windowWidth / 2);
        var y = cursor.Y + gap;

        x = windowWidth >= workArea.Width
            ? workArea.Left
            : Math.Clamp(x, workArea.Left, workArea.Right - windowWidth);
        y = windowHeight >= workArea.Height
            ? workArea.Top
            : Math.Clamp(y, workArea.Top, workArea.Bottom - windowHeight);

        return new Point(x, y);
    }

    public static string LastLog = "";

    /// <summary>
    /// Positions the window next to the cursor (see <see cref="CalculateNearCursorPosition"/>),
    /// always fully inside the work area of the monitor the cursor is on.
    /// </summary>
    public static void PositionWindowAtCursor(Window window, System.Drawing.Point? cursorOverride = null)
    {
        var cursor = cursorOverride ?? Control.MousePosition;
        var screen = Screen.FromPoint(cursor)
                     ?? Screen.PrimaryScreen
                     ?? (Screen.AllScreens.Length > 0 ? Screen.AllScreens[0] : null);

        if (screen == null)
        {
            LastLog = "screen is null";
            return;
        }

        double systemDpiScale = GetSystemDpiScale();
        double monitorDpiScale = GetMonitorDpiScale(screen);

        double widthDip = !double.IsNaN(window.Width) && window.Width > 0 ? window.Width : 400;
        double heightDip = window.ActualHeight > 0
            ? window.ActualHeight
            : ((window.Content as UIElement)?.DesiredSize.Height > 0
                ? (window.Content as UIElement)!.DesiredSize.Height + 50
                : 160);

        int physWidth = (int)Math.Round(widthDip * monitorDpiScale);
        int physHeight = (int)Math.Round(heightDip * monitorDpiScale);

        var wa = screen.WorkingArea;
        var near = CalculateNearCursorPosition(
            new Rect(wa.Left, wa.Top, wa.Width, wa.Height),
            new Point(cursor.X, cursor.Y),
            physWidth,
            physHeight,
            gap: 12 * monitorDpiScale);
        int physX = (int)Math.Round(near.X);
        int physY = (int)Math.Round(near.Y);

        window.Left = physX / systemDpiScale;
        window.Top = physY / systemDpiScale;

        var hwnd = new WindowInteropHelper(window).Handle;
        LastLog = $"Screen={screen.DeviceName}, Cursor={cursor}, Phys=[{physX},{physY}], Scale=[sys:{systemDpiScale},mon:{monitorDpiScale}], WinLeftTop=[{window.Left},{window.Top}], HWND={hwnd}";
        if (hwnd != IntPtr.Zero)
        {
            SetWindowPos(hwnd, IntPtr.Zero, physX, physY, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }
}
