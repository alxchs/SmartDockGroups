using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SmartDockGroups.App.Services;
using Xunit;

namespace SmartDockGroups.Tests;

public sealed class IconCacheServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SmartDockGroupsTests_Icon_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            try { Directory.Delete(_directory, recursive: true); } catch { }
        }
    }

    [Fact]
    public void TrimTransparentMargins_crops_uncentered_small_content_in_large_canvas()
    {
        // Create 256x256 transparent bitmap with a 60x60 square at (0, 0)
        int width = 256;
        int height = 256;
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];

        for (int y = 0; y < 60; y++)
        {
            for (int x = 0; x < 60; x++)
            {
                int offset = y * stride + x * 4;
                pixels[offset] = 255;     // B
                pixels[offset + 1] = 0;   // G
                pixels[offset + 2] = 0;   // R
                pixels[offset + 3] = 255; // A
            }
        }

        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        source.Freeze();

        var trimmed = IconCacheService.TrimTransparentMargins(source);

        Assert.Equal(60, trimmed.PixelWidth);
        Assert.Equal(60, trimmed.PixelHeight);
    }

    [Fact]
    public void TrimTransparentMargins_leaves_full_canvas_images_intact()
    {
        // Create 256x256 bitmap where image fills 240x240 centered
        int width = 256;
        int height = 256;
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];

        for (int y = 5; y < 250; y++)
        {
            for (int x = 5; x < 250; x++)
            {
                int offset = y * stride + x * 4;
                pixels[offset + 3] = 255; // A
            }
        }

        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        source.Freeze();

        var trimmed = IconCacheService.TrimTransparentMargins(source);

        // Content spans > 90% and is roughly centered, so kept at full canvas size
        Assert.Equal(256, trimmed.PixelWidth);
        Assert.Equal(256, trimmed.PixelHeight);
    }

    [Fact]
    public void TrimTransparentMargins_handles_fully_transparent_image_gracefully()
    {
        int width = 32;
        int height = 32;
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];

        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        source.Freeze();

        var trimmed = IconCacheService.TrimTransparentMargins(source);

        Assert.Equal(32, trimmed.PixelWidth);
        Assert.Equal(32, trimmed.PixelHeight);
    }

    [Theory]
    [InlineData("msteams://teams.microsoft.com/l/chat/0/0?users=test@example.com")]
    [InlineData("ms-settings:display")]
    [InlineData("https://www.google.com")]
    public void GetImageSource_resolves_icons_for_protocol_uris(string uri)
    {
        using var cache = new IconCacheService(_directory);
        var source = cache.GetImageSource(uri);

        Assert.NotNull(source);
        Assert.True(source.Width > 0);
        Assert.True(source.Height > 0);
    }

    [Fact]
    public void GetIcon_resolves_drawing_icon_for_protocol_uri()
    {
        using var cache = new IconCacheService(_directory);
        const string target = "msteams://teams.microsoft.com/l/chat/0/0?users=test@example.com";
        using var icon = cache.GetIcon(target);

        Assert.NotNull(icon);
        Assert.True(icon.Width > 0);
        Assert.True(icon.Height > 0);
    }
}
