using System.Drawing.Drawing2D;

namespace RectangleWinPlus;

/// <summary>Supplies the tray icon, loading the app's own icon.ico so the tray matches the exe.</summary>
internal static class TrayIcons
{
    /// <summary>The caller owns the returned icon and must pass it to <see cref="Destroy"/>.</summary>
    public static Icon Create()
    {
        try
        {
            var asm = typeof(TrayIcons).Assembly;
            string? name = Array.Find(asm.GetManifestResourceNames(),
                n => n.EndsWith("icon.ico", StringComparison.OrdinalIgnoreCase));

            if (name is not null)
            {
                using var stream = asm.GetManifestResourceStream(name);
                if (stream is not null)
                    // Picks the frame nearest the current DPI's tray size from the .ico.
                    return new Icon(stream, SystemInformation.SmallIconSize);
            }

            Log.Warn("icon.ico resource not found; drawing a fallback tray icon.");
        }
        catch (Exception ex)
        {
            Log.Error("Loading the tray icon failed; drawing a fallback", ex);
        }

        return DrawFallback();
    }

    public static void Destroy(Icon? icon) => icon?.Dispose();

    /// <summary>A plain glyph, only used if the embedded icon can't be loaded.</summary>
    private static Icon DrawFallback()
    {
        int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            float s = size;
            using var fill = new SolidBrush(Color.FromArgb(0x25, 0x63, 0xEB));
            using var tile = Rounded(new RectangleF(s * 0.06f, s * 0.06f, s * 0.88f, s * 0.88f), s * 0.22f);
            g.FillPath(fill, tile);

            using var white = new SolidBrush(Color.White);
            g.FillRectangle(white, s * 0.30f, s * 0.30f, s * 0.18f, s * 0.18f);
            using var pen = new Pen(Color.White, Math.Max(1f, s * 0.06f));
            g.DrawRectangle(pen, s * 0.30f, s * 0.30f, s * 0.40f, s * 0.40f);
        }

        // Clone off the GDI handle so the returned icon is fully managed and disposes cleanly.
        IntPtr handle = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            Native.DestroyIcon(handle);
        }
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
