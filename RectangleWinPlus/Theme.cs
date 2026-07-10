using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Microsoft.Win32;

namespace RectangleWinPlus;

/// <summary>Windows 11 surface colours, read from the user's own theme and accent settings.</summary>
internal static class Theme
{
    public static bool Dark { get; } = ReadAppsUseDarkTheme();

    public static Color Background => Dark ? Rgb(0x20, 0x20, 0x20) : Rgb(0xF3, 0xF3, 0xF3);
    public static Color Surface => Dark ? Rgb(0x2B, 0x2B, 0x2B) : Rgb(0xFF, 0xFF, 0xFF);
    public static Color Control => Dark ? Rgb(0x35, 0x35, 0x35) : Rgb(0xFB, 0xFB, 0xFB);
    public static Color ControlHover => Dark ? Rgb(0x3D, 0x3D, 0x3D) : Rgb(0xF2, 0xF2, 0xF2);
    public static Color ControlPressed => Dark ? Rgb(0x30, 0x30, 0x30) : Rgb(0xE8, 0xE8, 0xE8);
    public static Color Border => Dark ? Rgb(0x3A, 0x3A, 0x3A) : Rgb(0xE2, 0xE2, 0xE2);
    public static Color ControlBorder => Dark ? Rgb(0x45, 0x45, 0x45) : Rgb(0xD5, 0xD5, 0xD5);
    public static Color Text => Dark ? Rgb(0xFF, 0xFF, 0xFF) : Rgb(0x1B, 0x1B, 0x1B);
    public static Color Subtle => Dark ? Rgb(0x9E, 0x9E, 0x9E) : Rgb(0x5F, 0x5F, 0x5F);

    public static Color Accent { get; } = ReadAccent();
    public static Color AccentHover { get; } = Shade(Accent, Dark ? -0.10f : 0.12f);
    public static Color AccentPressed { get; } = Shade(Accent, Dark ? -0.20f : 0.24f);

    /// <summary>Black or white, whichever stays legible on the accent colour.</summary>
    public static Color OnAccent { get; } = Luminance(Accent) > 0.55 ? Rgb(0x00, 0x00, 0x00) : Rgb(0xFF, 0xFF, 0xFF);

    public static Font Body { get; } = new(PickFamily("Segoe UI Variable Text", "Segoe UI"), 9.75f);
    public static Font Header { get; } = new(PickFamily("Segoe UI Variable Display", "Segoe UI"), 10.5f, FontStyle.Bold);
    public static Font Mono { get; } = new(PickFamily("Cascadia Mono", "Consolas", "Courier New"), 9.25f);

    /// <summary>Rounded rectangle path, for cards and buttons.</summary>
    public static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = Math.Max(1, radius * 2);
        var path = new GraphicsPath();

        if (r.Width <= d || r.Height <= d) { path.AddRectangle(r); return path; }

        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Dark title bar and rounded window corners, matching the rest of the shell.</summary>
    public static void ApplyWindowChrome(IntPtr handle)
    {
        try
        {
            int dark = Dark ? 1 : 0;
            Native.DwmSetWindowAttribute(handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

            int round = Native.DWMWCP_ROUND;
            Native.DwmSetWindowAttribute(handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        }
        catch
        {
            // Older Windows: the window simply looks like it always did.
        }
    }

    private static Color Rgb(int r, int g, int b) => Color.FromArgb(r, g, b);

    private static bool ReadAppsUseDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch
        {
            return false;
        }
    }

    private static Color ReadAccent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int packed)
            {
                // Stored as 0xAABBGGRR.
                var accent = Color.FromArgb(packed & 0xFF, (packed >> 8) & 0xFF, (packed >> 16) & 0xFF);

                // A near-black or near-white accent would swallow the primary button; fall back.
                double luminance = Luminance(accent);
                if (luminance is > 0.04 and < 0.92) return accent;
            }
        }
        catch
        {
            // Fall through to the stock Windows blue.
        }

        return Dark ? Rgb(0x4C, 0xC2, 0xFF) : Rgb(0x00, 0x5F, 0xB8);
    }

    private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    /// <summary>Positive darkens, negative lightens.</summary>
    private static Color Shade(Color c, float amount)
    {
        if (amount >= 0)
            return Color.FromArgb((int)(c.R * (1 - amount)), (int)(c.G * (1 - amount)), (int)(c.B * (1 - amount)));

        float up = -amount;
        return Color.FromArgb(
            (int)(c.R + (255 - c.R) * up),
            (int)(c.G + (255 - c.G) * up),
            (int)(c.B + (255 - c.B) * up));
    }

    private static string PickFamily(params string[] candidates)
    {
        using var installed = new InstalledFontCollection();
        var names = installed.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return candidates.FirstOrDefault(names.Contains) ?? FontFamily.GenericSansSerif.Name;
    }
}
