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
    private double _progress;
    private bool _spinning;

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
        Loaded += (_, _) => Layout(face, note);

        foreach (var shape in new Shape[] { _track, _ring, _hole })
            shape.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Window), 1) });

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
        note.FontSize = faceSize * 0.4;
        DrawRing();
    }

    public ImageSource? Cover { set => Motion.CrossFade(_cover, _oldCoverBrush, _oldCover, value); }

    /// <summary>The hole shows the window's background through the record.</summary>
    public Brush? Hole { set => _hole.Fill = value; }

    /// <summary>Spins only while true; false freezes it where it is (keep it false while hidden to save CPU).</summary>
    public bool Spinning
    {
        set
        {
            if (value == _spinning) return;
            _spinning = value;
            if (value) _clock.Controller!.Resume();
            else _clock.Controller!.Pause();
        }
    }

    /// <summary>0..1 of the track played; drawn as the ring.</summary>
    public double Progress
    {
        set
        {
            value = Math.Clamp(double.IsFinite(value) ? value : 0, 0, 1);
            if (Math.Abs(value - _progress) < 0.001) return;
            _progress = value;
            DrawRing();
        }
    }

    private void DrawRing()
    {
        if (RingThickness <= 0 || double.IsNaN(Width)) return;
        double c = Width / 2, r = c - RingThickness / 2;
        if (_progress <= 0) { _ring.Data = null; return; }
        if (_progress >= 0.999) { _ring.Data = new EllipseGeometry(new Point(c, c), r, r); return; }
        double a = 2 * Math.PI * _progress;
        var figure = new PathFigure { StartPoint = new Point(c, c - r) };
        figure.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(a), c - r * Math.Cos(a)), new Size(r, r), 0, _progress > 0.5, SweepDirection.Clockwise, true));
        _ring.Data = new PathGeometry(new[] { figure });
    }
}
