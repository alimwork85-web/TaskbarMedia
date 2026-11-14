using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace TaskbarMedia;

/// <summary>
/// One line of text that slides back and forth when it is too long to fit (pause, slide, pause,
/// slide back), only while <see cref="Running"/> is true. Arabic text is laid out and scrolled
/// right-to-left, so a long Arabic title shows its beginning first instead of being cut there.
/// Font size/weight and color are inherited (e.g. TextElement.FontSize on this element).
/// </summary>
public sealed class MarqueeText : Panel
{
    private const double SpeedDipPerSecond = 30;
    private static readonly TimeSpan Pause = TimeSpan.FromSeconds(1.5);

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(MarqueeText),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure,
            (d, _) => ((MarqueeText)d).OnTextChanged()));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Center the text when it fits (used by the large panel).</summary>
    public bool CenterWhenFits { get; set; }

    // The TextBlock may be right-to-left (mirrored); the slide is applied to this
    // left-to-right host around it so the direction of the movement stays predictable.
    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.NoWrap };
    private readonly Border _host = new();
    private readonly TranslateTransform _shift = new();

    private bool _rtl;
    private bool _running;
    private double _overflow = -1;

    public MarqueeText()
    {
        ClipToBounds = true;
        _host.Child = _text;
        _host.RenderTransform = _shift;
        Children.Add(_host);
    }

    /// <summary>Slides only while true; set it when the owning window is shown/hidden.</summary>
    public bool Running
    {
        get => _running;
        set
        {
            if (_running == value) return;
            _running = value;
            Restart();
        }
    }

    private void OnTextChanged()
    {
        _text.Text = Text;
        _rtl = StartsRightToLeft(Text);
        _text.FlowDirection = _rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        _overflow = -1; // force a restart after the next arrange
    }

    protected override Size MeasureOverride(Size available)
    {
        _host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var desired = _host.DesiredSize;
        double width = double.IsInfinity(available.Width) ? desired.Width : Math.Min(desired.Width, available.Width);
        return new Size(width, desired.Height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double textWidth = _host.DesiredSize.Width;
        double overflow = Math.Max(0, Math.Ceiling(textWidth - final.Width));

        double x = overflow > 0
            ? (_rtl ? final.Width - textWidth : 0)                    // long: show where the text begins
            : CenterWhenFits ? (final.Width - textWidth) / 2
            : (_rtl ? final.Width - textWidth : 0);                   // short: align to its reading side
        _host.Arrange(new Rect(x, 0, textWidth, final.Height));

        if (overflow != _overflow)
        {
            _overflow = overflow;
            OpacityMask = overflow > 0 ? EdgeFade : null; // soft edges only when the text is cut
            Dispatcher.BeginInvoke(Restart, DispatcherPriority.Background);
        }
        return final;
    }

    private static readonly Brush EdgeFade = MakeEdgeFade();

    private static Brush MakeEdgeFade()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        b.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
        b.GradientStops.Add(new GradientStop(Colors.Black, 0.06));
        b.GradientStops.Add(new GradientStop(Colors.Black, 0.94));
        b.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        b.Freeze();
        return b;
    }

    private string? _pending;

    /// <summary>
    /// Changes the text with a short fade (out 90ms, swap, in 150ms); nothing happens when it is unchanged.
    /// A change during the fade-out just updates what is swapped in.
    /// </summary>
    public void FadeTo(string text)
    {
        if (text == (_pending ?? Text)) return;
        if (!IsVisible)
        {
            _pending = null;
            BeginAnimation(OpacityProperty, null);
            Text = text;
            return;
        }
        bool fading = _pending != null;
        _pending = text;
        if (fading) return;
        double rest = (double)GetAnimationBaseValue(OpacityProperty); // e.g. the artist line's 0.7
        var fadeOut = Motion.To(0, 90, Motion.EaseOut);
        fadeOut.Completed += (_, _) =>
        {
            if (_pending == null) return;
            Text = _pending;
            _pending = null;
            BeginAnimation(OpacityProperty, Motion.To(rest, 150, Motion.EaseOut));
        };
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private void Restart()
    {
        _shift.BeginAnimation(TranslateTransform.XProperty, null);
        _shift.X = 0;
        if (!_running || _overflow <= 0) return;

        // Right-to-left text starts on the right, so it slides right to reveal its end.
        double distance = _rtl ? _overflow : -_overflow;
        var slide = TimeSpan.FromSeconds(_overflow / SpeedDipPerSecond);
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };

        var t = TimeSpan.Zero;
        var animation = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(t)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(t += Pause)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(distance, KeyTime.FromTimeSpan(t += slide), ease));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(distance, KeyTime.FromTimeSpan(t += Pause)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(t += slide), ease));
        _shift.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    /// <summary>True when the first letter is Arabic (or another right-to-left script).</summary>
    private static bool StartsRightToLeft(string text)
    {
        foreach (char c in text)
        {
            if (!char.IsLetter(c)) continue;
            return c is >= '֐' and <= 'ࣿ'   // Hebrew, Arabic, Syriac, Thaana, Arabic supplements
                or >= 'יִ' and <= '﷿'          // presentation forms A
                or >= 'ﹰ' and <= '﻿';         // presentation forms B
        }
        return false;
    }
}
