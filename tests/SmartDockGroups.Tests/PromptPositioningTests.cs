using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using SmartDockGroups.App.Settings;
using Xunit;
using Point = System.Windows.Point;

namespace SmartDockGroups.Tests;

public sealed class PromptPositioningTests
{
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);

    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [Fact]
    public void CalculateCenteredPosition_Centers_Inside_WorkArea()
    {
        var workArea = new Rect(0, 40, 1920, 1040);
        var pos = PromptPositioning.CalculateCenteredPosition(workArea, 400, 160);

        // Expected X: 0 + (1920 - 400) / 2 = 760
        // Expected Y: 40 + (1040 - 160) / 2 = 40 + 440 = 480
        Assert.Equal(760, pos.X);
        Assert.Equal(480, pos.Y);
    }

    [Fact]
    public void CalculateCenteredPosition_SecondaryMonitor_Offset()
    {
        var workArea = new Rect(3840, 48, 1920, 1032);
        var pos = PromptPositioning.CalculateCenteredPosition(workArea, 400, 160);

        // Expected X: 3840 + (1920 - 400) / 2 = 3840 + 760 = 4600
        // Expected Y: 48 + (1032 - 160) / 2 = 48 + 436 = 484
        Assert.Equal(4600, pos.X);
        Assert.Equal(484, pos.Y);
    }

    [Fact]
    public void CalculateCenteredPosition_Clamps_When_Window_Exceeds_Bounds()
    {
        var workArea = new Rect(100, 100, 300, 200);
        // Window is larger than work area (500x300)
        var pos = PromptPositioning.CalculateCenteredPosition(workArea, 500, 300);

        Assert.Equal(100, pos.X);
        Assert.Equal(100, pos.Y);
    }

    [Theory]
    [InlineData(1.0, 100, 50, 1920, 1080, 100, 50, 1920, 1080)]
    [InlineData(1.5, 150, 75, 2880, 1620, 100, 50, 1920, 1080)]
    [InlineData(1.25, 125, 100, 2500, 1250, 100, 80, 2000, 1000)]
    public void PhysicalToDip_Scales_Accurately(
        double scale,
        int physX, int physY, int physW, int physH,
        double expX, double expY, double expW, double expH)
    {
        var physRect = new Rectangle(physX, physY, physW, physH);
        var dip = PromptPositioning.PhysicalToDip(physRect, scale, scale);

        Assert.Equal(expX, dip.X, 2);
        Assert.Equal(expY, dip.Y, 2);
        Assert.Equal(expW, dip.Width, 2);
        Assert.Equal(expH, dip.Height, 2);
    }

    [Fact]
    public void MultiMonitor_Real_Machine_Positions_On_Each_Screen()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                ExecuteMultiMonitorVerification();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null)
        {
            throw new AggregateException(error);
        }
    }

    private static void ExecuteMultiMonitorVerification()
    {
        if (System.Windows.Application.Current == null)
        {
            _ = new System.Windows.Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
        }
        else
        {
            System.Windows.Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        var app = System.Windows.Application.Current;
        if (app != null && app.Resources.MergedDictionaries.Count == 0)
        {
            app.Resources.MergedDictionaries.Add(
                new ResourceDictionary { Source = new Uri("pack://application:,,,/SmartDockGroups.App;component/Theming/Dark.xaml", UriKind.Absolute) });
            app.Resources.MergedDictionaries.Add(
                new ResourceDictionary { Source = new Uri("pack://application:,,,/SmartDockGroups.App;component/Theming/Icons.xaml", UriKind.Absolute) });
            app.Resources.MergedDictionaries.Add(
                new ResourceDictionary { Source = new Uri("pack://application:,,,/SmartDockGroups.App;component/Theming/Controls.xaml", UriKind.Absolute) });
        }

        var screens = Screen.AllScreens;
        Assert.True(screens.Length >= 1, "At least one monitor must be detected.");

        var logLines = new List<string>
        {
            $"MultiMonitor Verification Report - SmartDockGroups Defect B",
            $"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"Detected Screens Count: {screens.Length}",
            ""
        };

        for (int i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            logLines.Add($"--- Monitor {i} ({screen.DeviceName}) ---");
            logLines.Add($"Primary: {screen.Primary}");
            logLines.Add($"Physical Bounds: Left={screen.Bounds.Left}, Top={screen.Bounds.Top}, Width={screen.Bounds.Width}, Height={screen.Bounds.Height}");
            logLines.Add($"Physical WorkingArea: Left={screen.WorkingArea.Left}, Top={screen.WorkingArea.Top}, Width={screen.WorkingArea.Width}, Height={screen.WorkingArea.Height}");

            // Move real cursor to the middle of this monitor
            var cursorPoint = new System.Drawing.Point(
                screen.Bounds.Left + (screen.Bounds.Width / 2),
                screen.Bounds.Top + (screen.Bounds.Height / 2));

            SetCursorPos(cursorPoint.X, cursorPoint.Y);
            Cursor.Position = cursorPoint;
            Thread.Sleep(50);

            logLines.Add($"Physical Cursor Placed: [{cursorPoint.X}, {cursorPoint.Y}] (Verified MousePosition: [{Control.MousePosition.X}, {Control.MousePosition.Y}])");

            try
            {
                // Create TextPromptWindow passing explicit cursor for this monitor
                var prompt = new TextPromptWindow("Digite o novo nome do grupo:", "Novo Grupo", cursorPoint);

                prompt.Show();
                prompt.UpdateLayout();

                var hwnd = new WindowInteropHelper(prompt).Handle;
                GetWindowRect(hwnd, out var winRect);
                logLines.Add($"PromptPositioning.LastLog: {PromptPositioning.LastLog}");
                logLines.Add($"HWND: {hwnd}, WPF Window DIPs: Left={prompt.Left}, Top={prompt.Top}, Width={prompt.ActualWidth}, Height={prompt.ActualHeight}");
                logLines.Add($"Measured Win32 Physical Rect: [{winRect.Left}, {winRect.Top}, {winRect.Right}, {winRect.Bottom}] (Width={winRect.Right - winRect.Left}, Height={winRect.Bottom - winRect.Top})");

                var reportPath = @"C:\desenv\utils\SmartDockGroups\docs\execucoes\B-retangulos.txt";
                bool shouldWriteEvidence = !File.Exists(reportPath) || Environment.GetEnvironmentVariable("SMARTDOCK_GENERATE_EVIDENCE") == "1";

                if (shouldWriteEvidence)
                {
                    File.WriteAllLines(reportPath, logLines);
                }

            // Assert that the window physical rectangle is completely within the target screen bounds
            Assert.True(winRect.Left >= screen.Bounds.Left,
                $"Window Left ({winRect.Left}) is outside monitor {i} bounds ({screen.Bounds.Left})");
            Assert.True(winRect.Right <= screen.Bounds.Right,
                $"Window Right ({winRect.Right}) is outside monitor {i} bounds ({screen.Bounds.Right})");
            Assert.True(winRect.Top >= screen.Bounds.Top,
                $"Window Top ({winRect.Top}) is outside monitor {i} bounds ({screen.Bounds.Top})");
            Assert.True(winRect.Bottom <= screen.Bounds.Bottom,
                $"Window Bottom ({winRect.Bottom}) is outside monitor {i} bounds ({screen.Bounds.Bottom})");

            logLines.Add($"Assertion PASSED: Window is 100% contained within Monitor {i} ({screen.DeviceName})");

            // Capture screenshot of the dialog window
            try
            {
                int w = Math.Max(1, winRect.Right - winRect.Left);
                int h = Math.Max(1, winRect.Bottom - winRect.Top);
                using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    var hdc = g.GetHdc();
                    try
                    {
                        PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT);
                    }
                    finally
                    {
                        g.ReleaseHdc(hdc);
                    }
                }

                Directory.CreateDirectory(@"C:\desenv\utils\SmartDockGroups\docs\execucoes");
                var shotPath = $@"C:\desenv\utils\SmartDockGroups\docs\execucoes\B-monitor{i + 1}.png";
                if (!File.Exists(shotPath) || Environment.GetEnvironmentVariable("SMARTDOCK_GENERATE_EVIDENCE") == "1")
                {
                    bmp.Save(shotPath, ImageFormat.Png);
                }
                logLines.Add($"Captured Screenshot: {shotPath}");
            }
            catch (Exception ex)
            {
                logLines.Add($"Screenshot warning: {ex.Message}");
            }

            prompt.Close();
            logLines.Add("");
            }
            catch (Exception ex)
            {
                logLines.Add($"EXCEPTION on monitor {i}: {ex}");
                throw;
            }
        }

        var finalReportPath = @"C:\desenv\utils\SmartDockGroups\docs\execucoes\B-retangulos.txt";
        if (!File.Exists(finalReportPath) || Environment.GetEnvironmentVariable("SMARTDOCK_GENERATE_EVIDENCE") == "1")
        {
            File.WriteAllLines(finalReportPath, logLines);
        }
    }
}
