using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace RectangleWinPlus;

/// <summary>A rounded, bordered surface. Windows 11's card.</summary>
internal sealed class Card : Panel
{
    public Card()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Theme.Surface;
        Padding = new Padding(14, 10, 14, 12);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; init; } = 8;

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Backdrop.Behind(this));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundedRect(bounds, Radius);
        using var fill = new SolidBrush(Theme.Surface);
        using var pen = new Pen(Theme.Border);

        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(pen, path);

        base.OnPaint(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }
}

internal static class Backdrop
{
    /// <summary>
    /// The nearest opaque colour behind a control. Custom-painted controls must clear to this:
    /// clearing to Color.Transparent paints black, and a transparent parent's BackColor is useless.
    /// </summary>
    public static Color Behind(Control control)
    {
        for (Control? p = control.Parent; p is not null; p = p.Parent)
            if (p.BackColor.A != 0) return p.BackColor;

        return Theme.Background;
    }
}

internal enum ButtonKind
{
    /// <summary>Accent-filled. The one thing you probably came here to do.</summary>
    Primary,

    /// <summary>Bordered, neutral fill.</summary>
    Secondary,

    /// <summary>No fill or border until hovered.</summary>
    Ghost,

    /// <summary>A wide, left-aligned, monospaced field that shows a shortcut.</summary>
    Chip,
}

/// <summary>A flat, rounded, hover-aware button. WinForms' own Button cannot be styled this far.</summary>
internal class SoftButton : Button
{
    private bool _hover;
    private bool _pressed;

    public SoftButton(ButtonKind kind)
    {
        Kind = kind;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        BackColor = Theme.Background;
        Font = kind == ButtonKind.Chip ? Theme.Mono : Theme.Body;
        Height = kind == ButtonKind.Chip ? 32 : 32;
        Cursor = Cursors.Hand;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ButtonKind Kind { get; }

    /// <summary>Chips glow with the accent colour while they are capturing keys.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Highlighted { get; set; }

    /// <summary>Chips grey their text out when nothing is bound.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Muted { get; set; }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Backdrop.Behind(this));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        int radius = Kind == ButtonKind.Chip ? 6 : 5;
        using var path = Theme.RoundedRect(bounds, radius);

        (Color fill, Color border, Color text) = Palette();

        if (fill != Color.Transparent)
        {
            using var brush = new SolidBrush(fill);
            e.Graphics.FillPath(brush, path);
        }

        if (border != Color.Transparent)
        {
            using var pen = new Pen(border, Highlighted ? 1.6f : 1f);
            e.Graphics.DrawPath(pen, path);
        }

        var flags = Kind == ButtonKind.Chip
            ? TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
            : TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;

        var textArea = Kind == ButtonKind.Chip
            ? new Rectangle(11, 0, Width - 16, Height)
            : ClientRectangle;

        TextRenderer.DrawText(e.Graphics, Text, Font, textArea, text, flags);

        if (Focused && Kind != ButtonKind.Chip)
        {
            using var focus = new Pen(Theme.Accent) { DashStyle = DashStyle.Dot };
            using var inner = Theme.RoundedRect(Rectangle.Inflate(bounds, -3, -3), radius);
            e.Graphics.DrawPath(focus, inner);
        }
    }

    private (Color Fill, Color Border, Color Text) Palette()
    {
        Color state(Color rest, Color hover, Color pressed) => _pressed ? pressed : _hover ? hover : rest;

        return Kind switch
        {
            ButtonKind.Primary => (
                state(Theme.Accent, Theme.AccentHover, Theme.AccentPressed),
                Color.Transparent,
                Theme.OnAccent),

            ButtonKind.Secondary => (
                state(Theme.Control, Theme.ControlHover, Theme.ControlPressed),
                Theme.ControlBorder,
                Theme.Text),

            ButtonKind.Ghost => (
                _pressed ? Theme.ControlPressed : _hover ? Theme.ControlHover : Color.Transparent,
                Color.Transparent,
                Theme.Subtle),

            ButtonKind.Chip => (
                state(Theme.Control, Theme.ControlHover, Theme.ControlPressed),
                Highlighted ? Theme.Accent : Theme.ControlBorder,
                Muted ? Theme.Subtle : Theme.Text),

            _ => (Theme.Control, Theme.ControlBorder, Theme.Text),
        };
    }
}
