using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrawingIcon = System.Drawing.Icon;

namespace SmartDockGroups.App.Services;

/// <summary>
/// Produces the picture for a launch target.
///
/// Icons come from the shell's own image lists rather than from
/// <see cref="DrawingIcon.ExtractAssociatedIcon"/>: that API only ever returns a 32px
/// image, and round-tripping it through an .ico file on disk flattens the alpha channel,
/// which is what used to leave tiles sitting on a black or white square. The cache on
/// disk is therefore PNG, the one format here that keeps per-pixel transparency.
/// </summary>
internal sealed class IconCacheService : IDisposable
{
    private const int ShilJumbo = 0x4;
    private const int ShilExtraLarge = 0x2;
    private const uint ShgfiSysIconIndex = 0x000004000;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint FileAttributeNormal = 0x00000080;

    private static Guid _imageListId = new("46EB5926-582E-4017-9FDF-E8998DAA0950");
    private static Guid _shellItemImageFactoryId = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    private readonly string _cacheDirectory;
    private readonly Dictionary<string, DrawingIcon> _memoryCache = new();
    private readonly Dictionary<string, ImageSource?> _imageSourceCache = new();

    public IconCacheService(string cacheDirectory)
    {
        _cacheDirectory = cacheDirectory;
        Directory.CreateDirectory(_cacheDirectory);
    }

    public DrawingIcon? GetIcon(string targetPath)
    {
        if (_memoryCache.TryGetValue(targetPath, out var cached))
        {
            return cached;
        }

        var resolvedPath = ResolveFullPath(targetPath);
        DrawingIcon? icon = null;
        if (resolvedPath is not null)
        {
            try
            {
                icon = DrawingIcon.ExtractAssociatedIcon(resolvedPath);
            }
            catch (IOException)
            {
            }
            catch (ArgumentException)
            {
            }
        }

        if (icon is null)
        {
            var image = GetImageSource(targetPath);
            if (image is BitmapSource bs)
            {
                icon = BitmapSourceToIcon(bs);
            }
        }

        if (icon is not null)
        {
            _memoryCache[targetPath] = icon;
        }

        return icon;
    }

    /// <summary>Frozen WPF image for <paramref name="targetPath"/>, safe to share across windows.</summary>
    public ImageSource? GetImageSource(string targetPath)
    {
        if (_imageSourceCache.TryGetValue(targetPath, out var cached))
        {
            return cached;
        }

        var source = LoadImageSource(targetPath);
        _imageSourceCache[targetPath] = source;
        return source;
    }

    private ImageSource? LoadImageSource(string targetPath)
    {
        var cacheFilePath = Path.Combine(_cacheDirectory, BuildCacheKey(targetPath) + ".png");

        var fromDisk = LoadPngFromDisk(cacheFilePath);
        if (fromDisk is not null)
        {
            return TrimTransparentMargins(fromDisk);
        }

        var extracted = ExtractTargetIcon(targetPath);
        if (extracted is null)
        {
            return null;
        }

        if (extracted is BitmapSource bitmap)
        {
            extracted = TrimTransparentMargins(bitmap);
        }

        TryPersistPng(extracted, cacheFilePath);
        return extracted;
    }

    private static ImageSource? ExtractTargetIcon(string targetPath)
    {
        var resolvedPath = ResolveFullPath(targetPath);
        if (resolvedPath is not null)
        {
            if (resolvedPath.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
            {
                var url = ReadUrlFromShortcut(resolvedPath);
                if (!string.IsNullOrEmpty(url))
                {
                    var urlIcon = ExtractUriIcon(url);
                    if (urlIcon is not null)
                    {
                        return urlIcon;
                    }
                }
            }

            var fromShell = ExtractLargeIcon(resolvedPath) ?? ExtractFallbackIcon(resolvedPath);
            if (fromShell is not null)
            {
                return fromShell;
            }
        }

        return ExtractUriIcon(targetPath);
    }

    private static ImageSource? ExtractUriIcon(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        string scheme;
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            scheme = uri.Scheme;
        }
        else
        {
            var colonIndex = target.IndexOf(':');
            if (colonIndex <= 0)
            {
                return null;
            }
            scheme = target.Substring(0, colonIndex);
        }

        // 1. Try ASSOCSTR_DEFAULTICON (15)
        var iconRef = QueryAssociation(15, scheme);
        if (!string.IsNullOrEmpty(iconRef))
        {
            var loaded = LoadFromIconReference(iconRef);
            if (loaded is not null)
            {
                return loaded;
            }
        }

        // 2. Try ASSOCSTR_EXECUTABLE (1)
        var exePath = QueryAssociation(1, scheme);
        if (!string.IsNullOrEmpty(exePath))
        {
            if (exePath.StartsWith("\"") && exePath.IndexOf('\"', 1) is int end && end > 0)
            {
                exePath = exePath.Substring(1, end - 1);
            }
            if (File.Exists(exePath))
            {
                var exeIcon = ExtractLargeIcon(exePath) ?? ExtractFallbackIcon(exePath);
                if (exeIcon is not null)
                {
                    return exeIcon;
                }
            }
        }

        // 3. Try IShellItemImageFactory (handles shell:AppsFolder\... and shell items)
        return ExtractShellItemImage(target);
    }

    private static ImageSource? LoadFromIconReference(string iconRef)
    {
        if (string.IsNullOrWhiteSpace(iconRef))
        {
            return null;
        }

        // Indirect string: @{Package...}
        if (iconRef.StartsWith("@"))
        {
            var sb = new StringBuilder(1024);
            if (SHLoadIndirectString(iconRef, sb, (uint)sb.Capacity, IntPtr.Zero) == 0)
            {
                var resolved = sb.ToString();
                if (File.Exists(resolved))
                {
                    return LoadPngFromDisk(resolved);
                }
            }
        }

        // exe,index or direct file
        var parts = iconRef.Split(',');
        var filePath = parts[0].Trim('\"', ' ');
        if (File.Exists(filePath))
        {
            if (filePath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ||
                filePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                filePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                return ExtractLargeIcon(filePath) ?? ExtractFallbackIcon(filePath);
            }

            return LoadPngFromDisk(filePath);
        }

        return null;
    }

    private static string? QueryAssociation(int assocStr, string assoc)
    {
        try
        {
            uint len = 0;
            AssocQueryString(0, assocStr, assoc, "open", null, ref len);
            if (len == 0)
            {
                return null;
            }

            var sb = new StringBuilder((int)len);
            if (AssocQueryString(0, assocStr, assoc, "open", sb, ref len) == 0)
            {
                return sb.ToString();
            }
        }
        catch
        {
        }

        return null;
    }

    private static ImageSource? ExtractShellItemImage(string target)
    {
        try
        {
            if (SHCreateItemFromParsingName(target, IntPtr.Zero, ref _shellItemImageFactoryId, out var factory) == 0 && factory is not null)
            {
                try
                {
                    if (factory.GetImage(new SIZE(256, 256), SIIGBF.SIIGBF_ICONONLY, out var hbm) == 0 && hbm != IntPtr.Zero)
                    {
                        try
                        {
                            var bs = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                            bs.Freeze();
                            return bs;
                        }
                        finally
                        {
                            DeleteObject(hbm);
                        }
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(factory);
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static string? ReadUrlFromShortcut(string filePath)
    {
        try
        {
            foreach (var line in File.ReadAllLines(filePath))
            {
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                {
                    return line.Substring(4).Trim();
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }

    internal static BitmapSource TrimTransparentMargins(BitmapSource source)
    {
        var formatted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int width = formatted.PixelWidth;
        int height = formatted.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            return source;
        }

        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        formatted.CopyPixels(pixels, stride, 0);

        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * stride;
            for (int x = 0; x < width; x++)
            {
                byte alpha = pixels[rowOffset + x * 4 + 3];
                if (alpha > 10)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        if (maxX < minX || maxY < minY)
        {
            return source; // fully transparent
        }

        int boundW = maxX - minX + 1;
        int boundH = maxY - minY + 1;

        // If it already fills at least 90% of the canvas and is roughly centered, keep it intact
        if (boundW >= width * 0.9 && boundH >= height * 0.9 && minX <= width * 0.05 && minY <= height * 0.05)
        {
            return source;
        }

        var cropped = new CroppedBitmap(source, new Int32Rect(minX, minY, boundW, boundH));
        cropped.Freeze();
        return cropped;
    }

    private static DrawingIcon? BitmapSourceToIcon(BitmapSource bs)
    {
        try
        {
            using var ms = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bs));
            encoder.Save(ms);
            ms.Position = 0;
            using var bmp = new System.Drawing.Bitmap(ms);
            var hIcon = bmp.GetHicon();
            try
            {
                return (DrawingIcon)DrawingIcon.FromHandle(hIcon).Clone();
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The shell's jumbo list gives a 256px image with its alpha intact.</summary>
    private static ImageSource? ExtractLargeIcon(string path)
    {
        var info = default(ShFileInfo);
        var attributes = File.Exists(path) ? 0u : FileAttributeNormal;
        var flags = ShgfiSysIconIndex | (attributes == 0 ? 0 : ShgfiUseFileAttributes);

        if (SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), flags) == IntPtr.Zero)
        {
            return null;
        }

        foreach (var size in new[] { ShilJumbo, ShilExtraLarge })
        {
            if (SHGetImageList(size, ref _imageListId, out var list) != 0 || list is null)
            {
                continue;
            }

            try
            {
                if (list.GetIcon(info.iIcon, 0x00000001, out var hIcon) != 0 || hIcon == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    var source = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    return source;
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
            catch (COMException)
            {
            }
            finally
            {
                Marshal.ReleaseComObject(list);
            }
        }

        return null;
    }

    private static ImageSource? ExtractFallbackIcon(string path)
    {
        try
        {
            using var icon = DrawingIcon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (IOException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static BitmapSource? LoadPngFromDisk(string cacheFilePath)
    {
        if (!File.Exists(cacheFilePath))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            image.UriSource = new Uri(cacheFilePath);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (IOException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static void TryPersistPng(ImageSource source, string cacheFilePath)
    {
        if (source is not BitmapSource bitmap)
        {
            return;
        }

        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(cacheFilePath);
            encoder.Save(stream);
        }
        catch (IOException)
        {
            // A cache miss next time is the whole cost of failing here.
        }
    }

    private static string? ResolveFullPath(string targetPath)
    {
        if (File.Exists(targetPath) || Directory.Exists(targetPath))
        {
            return Path.GetFullPath(targetPath);
        }

        if (Path.IsPathRooted(targetPath))
        {
            return null;
        }

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, targetPath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public void Dispose()
    {
        foreach (var icon in _memoryCache.Values)
        {
            icon.Dispose();
        }

        _memoryCache.Clear();
        _imageSourceCache.Clear();
    }

    private static string BuildCacheKey(string targetPath)
    {
        var lastWriteTicks = File.Exists(targetPath) ? File.GetLastWriteTimeUtc(targetPath).Ticks : 0L;
        var identity = $"{targetPath.ToLowerInvariant()}|{lastWriteTicks}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexString(hash);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
        public SIZE(int cx, int cy) { this.cx = cx; this.cy = cy; }
    }

    [Flags]
    private enum SIIGBF
    {
        SIIGBF_RESIZETOFIT = 0x00,
        SIIGBF_ICONONLY = 0x04
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage([In] SIZE size, [In] SIIGBF flags, [Out] out IntPtr phbm);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    private interface IImageList
    {
        [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
        [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, ref int pi);
        [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
        [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
        [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, ref int pi);
        [PreserveSig] int Draw(IntPtr pimldp);
        [PreserveSig] int Remove(int i);
        [PreserveSig] int GetIcon(int i, int flags, out IntPtr picon);
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int AssocQueryString(int flags, int str, string pszAssoc, string pszExtra, StringBuilder? pszOut, ref uint pcchOut);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, uint cchOutBuf, IntPtr ppvReserved);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        [In, MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        [In] IntPtr pbc,
        [In] ref Guid riid,
        [Out, MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? ppv);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref ShFileInfo psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll")]
    private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList? ppv);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
