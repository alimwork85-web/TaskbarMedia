using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TaskbarMedia;

/// <summary>
/// Colors for the quick bar and the large panel: taken from the song's cover when it has a clear
/// color, otherwise the normal light/dark theme colors.
/// </summary>
internal sealed record Palette(Color Background, Brush Foreground, Brush Active, Brush Inactive)
{
    private const byte Alpha = 0xF5;

    public static Palette For(Color? coverColor)
    {
        if (coverColor is { } cover)
        {
            var bg = ColorExtractor.ToBackground(cover);
            var fg = ColorExtractor.TextOn(bg);
            return new Palette(
                Color.FromArgb(Alpha, bg.R, bg.G, bg.B),
                Frozen(fg),
                Frozen(fg),                                  // on a colored background, "on" = full text color
                Frozen(Color.FromArgb(0x99, fg.R, fg.G, fg.B))); // and "off" = dimmed
        }

        bool light = Theme.IsLight();
        var text = light ? Colors.Black : Colors.White;
        return new Palette(
            light ? Color.FromArgb(Alpha, 0xF9, 0xF9, 0xF9) : Color.FromArgb(Alpha, 0x2B, 0x2B, 0x2B),
            Frozen(text),
            Frozen(Theme.Accent()),
            Frozen(text));
    }

    /// <summary>Applies the palette to a window whose root border uses <paramref name="background"/>, fading the color.</summary>
    public void ApplyTo(Window window, SolidColorBrush background)
    {
        background.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(Background, TimeSpan.FromMilliseconds(400)) { EasingFunction = new QuadraticEase() });
        window.Foreground = Foreground;
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
