using System.Drawing;
using System.Drawing.Drawing2D;
using WinForms = System.Windows.Forms;

namespace TaskbarMedia;

/// <summary>
/// Draws the settings menu in the island's colors (from the song, like the capsule) instead of the classic
/// Windows look: flat colored background, rounded hover highlight, Fluent check marks and arrows,
/// and Windows 11 rounded corners with a shadow.
/// </summary>
internal sealed class MenuStyle : WinForms.ToolStripProfessionalRenderer
{
    private readonly Color _bg, _fg;

    private MenuStyle(Color bg, Color fg) : base(new Colors(bg)) { _bg = bg; _fg = fg; RoundedEdges = false; }

    /// <summary>Styles <paramref name="menu"/> (and its submenus) with the current song's palette.</summary>
    public static void Apply(WinForms.ContextMenuStrip menu, Palette palette)
    {
        var bg = palette.Background;
        var fg = ((System.Windows.Media.SolidColorBrush)palette.Foreground).Color;
        var style = new MenuStyle(Color.FromArgb(bg.R, bg.G, bg.B), Color.FromArgb(fg.R, fg.G, fg.B));

        menu.Renderer = style;
        menu.Font = new Font("Segoe UI", 10f);
        menu.Padding = new WinForms.Padding(4, 6, 4, 6);
        Style(menu.Items, style);
    }

    private static void Style(WinForms.ToolStripItemCollection items, MenuStyle style)
    {
        foreach (WinForms.ToolStripItem item in items)
        {
            item.ForeColor = style._fg;
            item.Padding = new WinForms.Padding(4, 5, 4, 5);
            if (item is WinForms.ToolStripMenuItem { HasDropDownItems: true } sub)
            {
                sub.DropDown.Renderer = style;
                sub.DropDown.Font = new Font("Segoe UI", 10f);
                sub.DropDown.Padding = new WinForms.Padding(4, 6, 4, 6);
                sub.DropDownOpened += (_, _) => Round(sub.DropDown.Handle);
                Style(sub.DropDownItems, style);
            }
        }
    }

    /// <summary>Windows 11 rounded corners and shadow for a menu window (ignored on Windows 10).</summary>
    public static void Round(IntPtr hwnd)
    {
        int round = 2; // DWMWCP_ROUND
        Native.DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, sizeof(int));
    }

    protected override void OnRenderMenuItemBackground(WinForms.ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var r = new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height);
        using var path = RoundRect(r, 6);
        using var brush = new SolidBrush(Color.FromArgb(0x30, _fg));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemCheck(WinForms.ToolStripItemImageRenderEventArgs e)
    {
        using var font = new Font("Segoe Fluent Icons", 9f);
        WinForms.TextRenderer.DrawText(e.Graphics, "", font, e.ImageRectangle, _fg,
            WinForms.TextFormatFlags.HorizontalCenter | WinForms.TextFormatFlags.VerticalCenter);
    }

    protected override void OnRenderArrow(WinForms.ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Color.FromArgb(0xB0, _fg);
        base.OnRenderArrow(e);
    }

    protected override void OnRenderSeparator(WinForms.ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(Color.FromArgb(0x30, _fg));
        e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
    }

    protected override void OnRenderItemText(WinForms.ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = _fg;
        base.OnRenderItemText(e);
    }

    private static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = radius * 2;
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>Flat background everywhere (no gradient image margin, no border line).</summary>
    private sealed class Colors(Color bg) : WinForms.ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => bg;
        public override Color ImageMarginGradientBegin => bg;
        public override Color ImageMarginGradientMiddle => bg;
        public override Color ImageMarginGradientEnd => bg;
        public override Color MenuBorder => bg;
        public override Color MenuItemBorder => Color.Transparent;
    }
}
