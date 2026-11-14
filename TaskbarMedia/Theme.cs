using System.Windows.Media;
using Microsoft.Win32;

namespace TaskbarMedia;

internal static class Theme
{
    /// <summary>The taskbar follows SystemUsesLightTheme (not AppsUseLightTheme).</summary>
    public static bool IsLight() =>
        Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "SystemUsesLightTheme", 0) is int v && v == 1;

    /// <summary>The Windows accent color (DWM stores it as 0xAABBGGRR).</summary>
    public static Color Accent()
    {
        if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "AccentColor", null) is int abgr)
            return Color.FromRgb((byte)abgr, (byte)(abgr >> 8), (byte)(abgr >> 16));
        return Color.FromRgb(0x4C, 0xC2, 0xFF);
    }

    /// <summary>
    /// The color the popups are tinted with: the cover's color, or the Windows accent in "Windows" mode.
    /// "Black" mode: the cover's color (or the accent) with alpha 0, which Palette.For reads as "black, with this accent";
    /// a distinct value, so switching modes or covers still triggers a recolor.
    /// </summary>
    public static Color? PopupSource(Color? coverColor) => Settings.AccentMode switch
    {
        "Windows" => Accent(),
        "Black" => (coverColor ?? Accent()) with { A = 0 },
        _ => coverColor,
    };
}
