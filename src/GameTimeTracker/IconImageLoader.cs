using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;

namespace GameTimeTracker;

internal static class IconImageLoader
{
    public static Bitmap? LoadBitmap(string? sourcePath, Size size)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return null;
        }

        try
        {
            var path = Environment.ExpandEnvironmentVariables(sourcePath.Trim());
            if (!File.Exists(path))
            {
                return null;
            }

            var extension = Path.GetExtension(path);
            if (extension.Equals(".ico", StringComparison.OrdinalIgnoreCase))
            {
                using var icon = new Icon(path, size);
                using var bitmap = icon.ToBitmap();
                return FitToCanvas(bitmap, size);
            }

            if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                using var icon = Icon.ExtractAssociatedIcon(path);
                if (icon is null)
                {
                    return null;
                }

                using var bitmap = icon.ToBitmap();
                return FitToCanvas(bitmap, size);
            }

            using var image = Image.FromFile(path);
            return FitToCanvas(image, size);
        }
        catch
        {
            return null;
        }
    }

    public static string? CacheExecutableIcon(string? executablePath, Size size)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        try
        {
            var path = Environment.ExpandEnvironmentVariables(executablePath.Trim());
            if (!Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            {
                return null;
            }

            Directory.CreateDirectory(AppPaths.IconCacheDirectory);
            var destination = Path.Combine(AppPaths.IconCacheDirectory, CreateCachedIconFileName(path));
            if (File.Exists(destination))
            {
                return destination;
            }

            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }

            using var iconBitmap = icon.ToBitmap();
            using var cachedBitmap = FitToCanvas(iconBitmap, size);
            cachedBitmap.Save(destination, ImageFormat.Png);
            return destination;
        }
        catch
        {
            return null;
        }
    }

    public static Bitmap CreateFallback(Size size)
    {
        var bitmap = new Bitmap(size.Width, size.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        var bounds = new Rectangle(3, 3, size.Width - 6, size.Height - 6);
        using var fill = new SolidBrush(Color.FromArgb(235, 239, 245));
        using var border = new Pen(Color.FromArgb(154, 164, 178), 1);
        graphics.FillEllipse(fill, bounds);
        graphics.DrawEllipse(border, bounds);

        using var glyphBrush = new SolidBrush(Color.FromArgb(82, 92, 107));
        var fontFamily = SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif;
        using var font = new Font(fontFamily, Math.Max(8, size.Height / 3), FontStyle.Bold);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString("G", font, glyphBrush, bounds, format);

        return bitmap;
    }

    private static string CreateCachedIconFileName(string executablePath)
    {
        var file = new FileInfo(executablePath);
        var safeName = string.Concat(
            Path.GetFileNameWithoutExtension(executablePath)
                .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        var identity = $"{file.FullName.ToUpperInvariant()}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..12].ToLowerInvariant();
        return $"{safeName}-{hash}.png";
    }

    private static Bitmap FitToCanvas(Image image, Size size)
    {
        var bitmap = new Bitmap(size.Width, size.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);

        var scale = Math.Min((double)size.Width / image.Width, (double)size.Height / image.Height);
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));
        var x = (size.Width - width) / 2;
        var y = (size.Height - height) / 2;

        graphics.DrawImage(image, new Rectangle(x, y, width, height));
        return bitmap;
    }
}
