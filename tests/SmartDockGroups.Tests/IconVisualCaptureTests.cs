using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SmartDockGroups.App.Desktop;
using SmartDockGroups.App.Menu;
using SmartDockGroups.App.Services;
using SmartDockGroups.Core.Models;
using Xunit;

namespace SmartDockGroups.Tests;

public sealed class IconVisualCaptureTests
{
    [Fact]
    public void Render_Teams_AppFolderTile_After_Fix()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                ExecuteRender();
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

    private static void ExecuteRender()
    {
        var category = new MenuCategory
        {
            Name = "Teams Chat",
            DisplayMode = DesktopGroupDisplayMode.AppFolder,
            DesktopIconScale = 1.0,
            AreaOpacity = 1.0,
            TitleOpacity = 1.0
        };

        category.Items.Add(new LaunchItem
        {
            Name = "Alexandre",
            Type = LaunchItemType.File,
            Target = "msteams://teams.microsoft.com/l/chat/0/0?users=alexandre.sousa@iob.com.br",
            ExecutionMode = ExecutionMode.Normal,
            IsDesktopPinned = true
        });

        var theme = new MenuTheme
        {
            BackgroundColor = "#004040",
            Opacity = 0.97,
            BorderColor = "#3C3C3C",
            CornerRadius = 6,
            ShowShadow = true,
            ShadowBlurRadius = 12,
            ShadowDepth = 2,
            ShadowDirection = 315,
            ShadowOpacity = 0.35,
            ItemSpacing = 2,
            ItemPadding = 8,
            IconSize = 18,
            TextColor = "#FFFFFF",
            HighlightColor = "#3D7EB8FF",
            TitleFontFamily = "Segoe UI",
            TitleFontSize = 13,
            TitleBold = true,
            ItemFontFamily = "Segoe UI",
            ItemFontSize = 13,
            AnimationDurationMs = 120
        };

        var cacheDir = Path.Combine(Path.GetTempPath(), "SmartDockGroupsTests_Capture_" + Guid.NewGuid().ToString("N"));
        try
        {
            using var iconCache = new IconCacheService(cacheDir);

            // Build the tile
            var tile = AppFolderTile.Build(category, theme, iconCache, scale: 1.0);

            // Container matching the 160x180 crop
            var container = new Border
            {
                Width = 160,
                Height = 180,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 30, 45, 50)), // matching dark desktop backdrop
                Child = tile
            };

            container.Measure(new System.Windows.Size(160, 180));
            container.Arrange(new Rect(0, 0, 160, 180));
            container.UpdateLayout();

            var rtb = new RenderTargetBitmap(160, 180, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(container);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));

            var outDir = @"C:\desenv\utils\SmartDockGroups\docs\execucoes";
            Directory.CreateDirectory(outDir);
            var outPath = Path.Combine(outDir, "A-depois.png");

            using (var stream = File.Create(outPath))
            {
                encoder.Save(stream);
            }

            Assert.True(File.Exists(outPath));
        }
        finally
        {
            if (Directory.Exists(cacheDir))
            {
                try { Directory.Delete(cacheDir, recursive: true); } catch { }
            }
        }
    }
}
