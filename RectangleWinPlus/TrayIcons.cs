using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace RectangleWinPlus;

/// <summary>Draws the tray glyph at runtime so the app ships without binary assets.</summary>
internal static class TrayIcons
{
    /// <summary>The caller owns the returned icon and must pass it to <see cref="Destroy"/>.</summary>
    public static Icon Create()
    {
        Color ink = TaskbarIsLight() ? Color.FromArgb(32, 32, 32) : Color.White;

        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var pen = new Pen(ink, 2.5f);
            g.DrawRectangle(pen, 3, 4, 26, 24);

            // A filled top-left quadrant: the app's whole idea in one glyph.
            using var brush = new SolidBrush(ink);
            g.FillRectangle(brush, 5, 6, 11, 9);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    public static void Destroy(Icon? icon)
    {
        if (icon is null) return;
        IntPtr handle = icon.Handle;
        icon.Dispose();
        Native.DestroyIcon(handle);
    }

    private static bool TaskbarIsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
        }
        catch
        {
            return false;
        }
    }
}
