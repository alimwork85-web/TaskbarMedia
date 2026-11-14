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

/// <summary>Shared motion curves and durations, so every animation in the app moves the same way.</summary>
public static class Motion
{
    public static readonly KeySpline EaseOut = Frozen(new KeySpline(0.23, 1, 0.32, 1));
    public static readonly KeySpline EaseMorph = Frozen(new KeySpline(0.32, 0.72, 0, 1));

    /// <summary>Windows "Animation effects" is off: fades only, no scaling, sliding or spinning.</summary>
    public static bool Reduced => !SystemParameters.ClientAreaAnimation;

    /// <summary>A single-segment animation to <paramref name="to"/> along <paramref name="curve"/>.</summary>
    public static DoubleAnimationUsingKeyFrames To(double to, double ms, KeySpline curve, double? from = null)
    {
        var a = new DoubleAnimationUsingKeyFrames();
        if (from is { } f) a.KeyFrames.Add(new DiscreteDoubleKeyFrame(f, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        a.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), curve));
        return a;
    }

    /// <summary>
    /// Sets an IconButton's glyph; when it actually changes, the new glyph fades in growing from 0.8
    /// (the "Glyph" presenter in App.xaml's IconButton template).
    /// </summary>
    public static void SwapGlyph(ContentControl button, string glyph)
    {
        if (Equals(button.Content, glyph)) return;
        button.Content = glyph;
        if (button.Template?.FindName("Glyph", button) is not UIElement g) return;

        g.BeginAnimation(UIElement.OpacityProperty, To(1, 140, EaseOut, 0));
        if (Reduced) return;
        if (g.RenderTransform is not ScaleTransform s || s.IsFrozen)
            g.RenderTransform = s = new ScaleTransform();
        g.RenderTransformOrigin = new Point(0.5, 0.5);
        s.BeginAnimation(ScaleTransform.ScaleXProperty, To(1, 140, EaseOut, 0.8));
        s.BeginAnimation(ScaleTransform.ScaleYProperty, To(1, 140, EaseOut, 0.8));
    }

    /// <summary>
    /// Shows <paramref name="next"/> in <paramref name="brush"/>, fading the previous image out (200ms) on
    /// <paramref name="oldLayer"/>, a layer painted with <paramref name="oldBrush"/> above it. Same image: nothing happens.
    /// </summary>
    public static void CrossFade(ImageBrush brush, ImageBrush oldBrush, UIElement oldLayer, ImageSource? next)
    {
        if (ReferenceEquals(brush.ImageSource, next)) return;
        if (brush.ImageSource is { } previous)
        {
            oldBrush.ImageSource = previous;
            var fade = To(0, 200, EaseOut, 1);
            fade.Completed += (_, _) => { if (oldLayer.Opacity == 0) oldBrush.ImageSource = null; };
            oldLayer.BeginAnimation(UIElement.OpacityProperty, fade);
        }
        brush.ImageSource = next;
    }

    /// <summary>
    /// Moves a progress value to <paramref name="to"/> in a straight line over <paramref name="ms"/> (the
    /// refresh interval), so it creeps instead of ticking. Jumps instead for big moves (over 3% of
    /// <paramref name="full"/>), any move back (a seek or a new song), or <paramref name="ms"/> = 0.
    /// </summary>
    public static void Glide(IAnimatable target, DependencyProperty property, double to, double ms, double full)
    {
        var element = (DependencyObject)target;
        double now = (double)element.GetValue(property);
        if (Math.Abs(to - now) < full * 0.0005) return;
        if (ms <= 0 || to < now || to - now > full * 0.03)
        {
            target.BeginAnimation(property, null);
            element.SetValue(property, to);
            return;
        }
        target.BeginAnimation(property, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)));
    }

    private static KeySpline Frozen(KeySpline k)
    {
        k.Freeze();
        return k;
    }
}
