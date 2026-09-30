using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SmartDockGroups.App.Services;
using SmartDockGroups.Core.Models;
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

    [Fact]
    public void TrimTransparentMargins_applied_twice_on_real_png_does_not_throw_or_zero_out()
    {
        var pngPath = @"C:\desenv\utils\SmartDockGroups\tests\runs\iconcache_original_backup\2223BC1DEA052500010F046078B4C8FB8EEFDA648055278E1948F0D6F7E7E6B1.png";
        if (!File.Exists(pngPath)) return;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        image.UriSource = new Uri(pngPath);
        image.EndInit();
        image.Freeze();

        var firstTrim = IconCacheService.TrimTransparentMargins(image);
        Assert.NotNull(firstTrim);
        Assert.True(firstTrim.PixelWidth > 0);
        Assert.True(firstTrim.PixelHeight > 0);

        var secondTrim = IconCacheService.TrimTransparentMargins(firstTrim);
        Assert.NotNull(secondTrim);
        Assert.True(secondTrim.PixelWidth > 0);
        Assert.True(secondTrim.PixelHeight > 0);
        Assert.Equal(firstTrim.PixelWidth, secondTrim.PixelWidth);
        Assert.Equal(firstTrim.PixelHeight, secondTrim.PixelHeight);

        // Test BitmapSourceToIcon via GetIcon logic
        using var ms = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(secondTrim));
        encoder.Save(ms);
        ms.Position = 0;
        using var bmp = new System.Drawing.Bitmap(ms);
        var hIcon = bmp.GetHicon();
        Assert.NotEqual(IntPtr.Zero, hIcon);
        using var icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(hIcon).Clone();
        Assert.NotNull(icon);
        Assert.True(icon.Width > 0);
        Assert.True(icon.Height > 0);
    }

    [Fact]
    public void BuildCacheKey_uses_v3_schema_so_images_cached_by_earlier_builds_are_extracted_again()
    {
        using var cache = new IconCacheService(_directory);
        const string uri = "msteams://teams.microsoft.com/l/chat/0/0?users=test@example.com";

        string Hash(string identity) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)));

        // v1 had no prefix; v2 (1.1.1.2) fixed URIs; v3 draws "use the program's icon" shortcuts from the program.
        var legacyHash = Hash($"{uri.ToLowerInvariant()}|0");
        var v2Hash = Hash($"v2:{uri.ToLowerInvariant()}|0");
        var v3Hash = Hash($"v3:{uri.ToLowerInvariant()}|0");

        Assert.NotEqual(legacyHash, v3Hash);
        Assert.NotEqual(v2Hash, v3Hash);

        cache.GetImageSource(uri);
        Assert.True(File.Exists(Path.Combine(_directory, v3Hash + ".png")));
        Assert.False(File.Exists(Path.Combine(_directory, v2Hash + ".png")));
    }

    [Fact]
    public void GetImageSource_resolves_renamed_desktop_url_shortcut()
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), "SmartDockGroupsTests_Rename_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        try
        {
            var renamedFile = Path.Combine(tempFolder, "Alexandre Chagas Sousa.url");
            File.WriteAllText(renamedFile, "[InternetShortcut]\nURL=msteams://teams.microsoft.com/l/chat/0/0?users=alexandre.sousa@iob.com.br\n");

            // Look up via original target name Alexandre.url (which does not exist)
            var originalTarget = Path.Combine(tempFolder, "Alexandre.url");
            Assert.False(File.Exists(originalTarget));

            // Confirm ResolveFullPath and ShellCommands resolve unequivocally to the single matching file
            var resolvedPath = IconCacheService.ResolveFullPath(originalTarget);
            Assert.Equal(renamedFile, resolvedPath);

            var item = new LaunchItem { Name = "Alexandre", Target = originalTarget, Type = LaunchItemType.File };
            Assert.Equal(renamedFile, ShellCommands.ResolveTarget(item));
            Assert.True(ShellCommands.TryResolveTarget(item, out var shellResolved));
            Assert.Equal(renamedFile, shellResolved);

            using var cache = new IconCacheService(_directory);
            var image = cache.GetImageSource(originalTarget);
            Assert.NotNull(image);
            Assert.True(image.Width > 0);

            using var icon = cache.GetIcon(originalTarget);
            Assert.NotNull(icon);
            Assert.True(icon.Width > 0);
        }
        finally
        {
            if (Directory.Exists(tempFolder))
            {
                try { Directory.Delete(tempFolder, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public void ResolveFullPath_and_TryResolveTarget_return_null_when_multiple_candidates_match_prefix_ambiguity()
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), "SmartDockGroupsTests_Ambiguity_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        try
        {
            // Create two candidates: Alvo A.url and Alvo B.url (neither named Alvo.url)
            var fileA = Path.Combine(tempFolder, "Alvo A.url");
            var fileB = Path.Combine(tempFolder, "Alvo B.url");
            File.WriteAllText(fileA, "[InternetShortcut]\nURL=https://example.com/a\n");
            File.WriteAllText(fileB, "[InternetShortcut]\nURL=https://example.com/b\n");

            var queryTarget = Path.Combine(tempFolder, "Alvo.url");
            Assert.False(File.Exists(queryTarget));

            // 1. IconCacheService.ResolveFullPath must return null (ambiguity treated as unresolved)
            var resolvedIconPath = IconCacheService.ResolveFullPath(queryTarget);
            Assert.Null(resolvedIconPath);

            // 2. IconCacheService GetImageSource and GetIcon must also return null without picking arbitrarily
            using var cache = new IconCacheService(_directory);
            var image = cache.GetImageSource(queryTarget);
            Assert.Null(image);

            using var icon = cache.GetIcon(queryTarget);
            Assert.Null(icon);

            // 3. ShellCommands.ResolveTarget and TryResolveTarget must return null/false
            var item = new LaunchItem { Name = "Alvo", Target = queryTarget, Type = LaunchItemType.File };
            var resolvedShellTarget = ShellCommands.ResolveTarget(item);
            Assert.Null(resolvedShellTarget);

            var canResolve = ShellCommands.TryResolveTarget(item, out var resolvedPath);
            Assert.False(canResolve);
            Assert.Empty(resolvedPath);
        }
        finally
        {
            if (Directory.Exists(tempFolder))
            {
                try { Directory.Delete(tempFolder, recursive: true); } catch { }
            }
        }
    }
}
