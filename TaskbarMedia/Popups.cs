using System.Runtime.InteropServices;
using System.Windows;

namespace TaskbarMedia;

internal static class Popups
{
    /// <summary>
    /// Center: centered on the anchor. LeanLeft / LeanRight: halfway between centered and
    /// lining up with the anchor's right / left edge, so the window leans to that side.
    /// </summary>
    public enum Align { Center, LeanLeft, LeanRight }

    /// <summary>
    /// Moves an already-shown window so it sits above <paramref name="anchor"/> (screen px),
    /// resting on the taskbar and kept inside the monitor's work area.
    /// <paramref name="exactX"/>, when given, is the window's left edge and overrides <paramref name="align"/>.
    /// </summary>
    public static void PlaceAbove(Window window, IntPtr hwnd, Native.RECT anchor, Align align = Align.Center, int? exactX = null)
    {
        double scale = Native.GetDpiForWindow(hwnd) / 96.0;
        if (scale <= 0) scale = 1;
        int width = (int)Math.Ceiling(window.ActualWidth * scale);
        int height = (int)Math.Ceiling(window.ActualHeight * scale);

        var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        Native.GetMonitorInfo(Native.MonitorFromRect(ref anchor, Native.MONITOR_DEFAULTTONEAREST), ref info);
        var work = info.rcWork;

        int centered = (anchor.Left + anchor.Right) / 2 - width / 2;
        int x = exactX ?? align switch
        {
            Align.LeanLeft => (centered + anchor.Right - width) / 2,
            Align.LeanRight => (centered + anchor.Left) / 2,
            _ => centered,
        };
        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width));
        int y = work.Bottom - height;

        // If something minimized the popup (e.g. Win+D, or tools that hide windows), it would
        // reappear as a tiny clipped minimized-size window; restore it first.
        if (Native.IsIconic(hwnd))
            Native.ShowWindow(hwnd, Native.SW_SHOWNOACTIVATE);

        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, width, height,
            Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }
}
