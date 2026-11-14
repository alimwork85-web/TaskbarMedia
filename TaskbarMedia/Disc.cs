using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace TaskbarMedia;

/// <summary>
/// The cover as a record: a round cover that spins while playing (and stops in place when paused),
/// a hole in the middle, and an optional progress ring around it. Ring and hole follow the window's colors.
/// </summary>
public sealed class Disc : Grid
{
    private readonly ImageBrush _cover = new() { Stretch = Stretch.UniformToFill };
    private readonly ImageBrush _oldCoverBrush = new() { Stretch = Stretch.UniformToFill };
    private readonly Ellipse _oldCover = new() { Opacity = 0, IsHitTestVisible = false };
    private readonly RotateTransform _rotate = new();
    private readonly AnimationClock _clock;
    private readonly Ellipse _track = new() { Opacity = 0.25 };
    private readonly Path _ring = new() { StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly Ellipse _hole = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Ellipse _thumb = new() { Opacity = 0, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private bool _spinning;
    private bool _thumbShown;

    /// <summary>Ring thickness; 0 = no ring.</summary>
    public double RingThickness { get; set; }

    /// <summary>Gap between the ring and the cover.</summary>
    public double RingGap { get; set; } = 2;

    public Disc()
    {
        FlowDirection = FlowDirection.LeftToRight; // the ring always runs clockwise
        var note = new TextBlock
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.7,
        };
        note.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        var placeholder = new Grid();
        placeholder.Children.Add(new Ellipse { Fill = new SolidColorBrush(Color.FromArgb(0x33, 0x80, 0x80, 0x80)) });
        placeholder.Children.Add(note);

        var cover = new Ellipse { Fill = _cover, RenderTransform = _rotate, RenderTransformOrigin = new Point(0.5, 0.5) };
        var face = new Grid();
        face.Children.Add(placeholder);
        face.Children.Add(cover);
        _oldCover.Fill = _oldCoverBrush; // the previous song's cover, fading out over the new one
        _oldCover.RenderTransform = _rotate;
        _oldCover.RenderTransformOrigin = new Point(0.5, 0.5);
        face.Children.Add(_oldCover);
        face.Children.Add(_hole);
        Children.Add(_track);
        Children.Add(_ring);
        Children.Add(face);
        Children.Add(_thumb);
        Loaded += (_, _) => Layout(face, note);

        foreach (var shape in new Shape[] { _track, _ring, _hole })
            shape.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Window), 1) });
        _thumb.SetBinding(Shape.FillProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Window), 1) });

        _clock = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(6)) { RepeatBehavior = RepeatBehavior.Forever }.CreateClock();
        _rotate.ApplyAnimationClock(RotateTransform.AngleProperty, _clock);
        _clock.Controller!.Pause();
    }

    private void Layout(Grid face, TextBlock note)
    {
        double inset = RingThickness > 0 ? RingThickness + RingGap : 0;
        face.Margin = new Thickness(inset);
        _track.StrokeThickness = _ring.StrokeThickness = RingThickness;
        _track.Visibility = _ring.Visibility = RingThickness > 0 ? Visibility.Visible : Visibility.Collapsed;
        double faceSize = Width - 2 * inset;
        _hole.Width = _hole.Height = faceSize * 0.28;
        _hole.StrokeThickness = 0;
        _thumb.Width = _thumb.Height = RingThickness * 2.5;
        note.FontSize = faceSize * 0.4;
        DrawRing();
    }

    public ImageSource? Cover { set => Motion.CrossFade(_cover, _oldCoverBrush, _oldCover, value); }

    /// <summary>The hole shows the window's background through the record.</summary>
    public Brush? Hole { set => _hole.Fill = value; }

    /// <summary>
    /// Spins only while true; false slows it to a stop where it is (keep it false while hidden to save CPU).
    /// Speeding up and slowing down take 500ms. With reduced motion it never spins.
    /// </summary>
    public bool Spinning
    {
        set
        {
            if (value == _spinning) return;
            _spinning = value;
            if (Motion.Reduced)
            {
                _clock.Controller!.Pause();
                return;
            }
            if (value)
            {
                _clock.Controller!.SpeedRatio = Math.Max(_speed, MinSpeed);
                _clock.Controller.Resume();
            }
            if (_ramping) return;
            _ramping = true;
            _rampLast = DateTime.UtcNow;
            CompositionTarget.Rendering += Ramp; // detached again as soon as the speed settles
        }
    }

    private const double MinSpeed = 0.01; // the clock's speed ratio must stay above 0
    private double _speed;
    private bool _ramping;
    private DateTime _rampLast;

    private void Ramp(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        double step = (now - _rampLast).TotalMilliseconds / 500;
        _rampLast = now;
        double target = _spinning ? 1 : 0;
        _speed = target > _speed ? Math.Min(target, _speed + step) : Math.Max(target, _speed - step);
        if (_speed == target)
        {
            CompositionTarget.Rendering -= Ramp;
            _ramping = false;
            if (_speed == 0) _clock.Controller!.Pause();
        }
        if (_speed > 0) _clock.Controller!.SpeedRatio = Math.Max(_speed, MinSpeed);
    }

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(Disc), new PropertyMetadata(0.0, (d, _) => ((Disc)d).DrawRing()));

    /// <summary>0..1 of the track played; drawn as the ring. Animatable (see Motion.Glide).</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    /// <summary>Shows a dot at the head of the ring (fades 120ms): while the pointer is over the ring or dragging it.</summary>
    public bool ShowThumb
    {
        set
        {
            if (value == _thumbShown) return;
            _thumbShown = value;
            _thumb.BeginAnimation(OpacityProperty, Motion.To(value ? 1 : 0, 120, Motion.EaseOut));
        }
    }

    private void DrawRing()
    {
        if (RingThickness <= 0 || double.IsNaN(Width)) return;
        double progress = Math.Clamp(double.IsFinite(Progress) ? Progress : 0, 0, 1);
        double c = Width / 2, r = c - RingThickness / 2;
        double a = 2 * Math.PI * progress;
        var head = new Point(c + r * Math.Sin(a), c - r * Math.Cos(a));
        _thumb.Margin = new Thickness(head.X - _thumb.Width / 2, head.Y - _thumb.Height / 2, 0, 0);
        if (progress <= 0) { _ring.Data = null; return; }
        if (progress >= 0.999) { _ring.Data = new EllipseGeometry(new Point(c, c), r, r); return; }
        var figure = new PathFigure { StartPoint = new Point(c, c - r) };
        figure.Segments.Add(new ArcSegment(head, new Size(r, r), 0, progress > 0.5, SweepDirection.Clockwise, true));
        _ring.Data = new PathGeometry(new[] { figure });
    }
}
