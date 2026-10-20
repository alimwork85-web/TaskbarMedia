using System.Runtime.InteropServices;
using System.Windows;

namespace TaskbarMedia;

internal static class Popups
{
    /// <summary>
    /// Moves an already-shown window to <paramref name="anchor"/> (screen px): centered on it, with its top edge on
    /// anchor.Top (<paramref name="top"/>) or its bottom edge on anchor.Bottom, and kept inside the monitor's work area.
    /// </summary>
    public static void Place(Window window, IntPtr hwnd, Native.RECT anchor, bool top)
    {
        double scale = Native.GetDpiForWindow(hwnd) / 96.0;
        if (scale <= 0) scale = 1;
        int width = (int)Math.Ceiling(window.ActualWidth * scale);
        int height = (int)Math.Ceiling(window.ActualHeight * scale);

        var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        Native.GetMonitorInfo(Native.MonitorFromRect(ref anchor, Native.MONITOR_DEFAULTTONEAREST), ref info);
        var work = info.rcWork;

        int x = Math.Clamp((anchor.Left + anchor.Right) / 2 - width / 2, work.Left, Math.Max(work.Left, work.Right - width));
        int y = Math.Clamp(top ? anchor.Top : anchor.Bottom - height, work.Top, Math.Max(work.Top, work.Bottom - height));

        // If something minimized the popup (e.g. Win+D, or tools that hide windows), it would
        // reappear as a tiny clipped minimized-size window; restore it first.
        if (Native.IsIconic(hwnd))
            Native.ShowWindow(hwnd, Native.SW_SHOWNOACTIVATE);

        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, width, height,
            Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }
}
