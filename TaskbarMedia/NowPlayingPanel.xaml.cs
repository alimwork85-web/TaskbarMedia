using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Windows.Media;

namespace TaskbarMedia;

/// <summary>
/// The large now-playing panel the island opens into.
/// Closes when you click anywhere outside it (it loses activation) or press Esc.
/// </summary>
public partial class NowPlayingPanel : Window
{
    private readonly MediaService _media;
    private readonly Action<MediaState> _onUpdated;
    private readonly SolidColorBrush _background = new(Colors.Transparent);
    private Palette _palette = Palette.For(null);
    private Color? _paletteSource;
    private IntPtr _hwnd;

    // Seek bar: moves on its own while the panel is open; while dragging it follows the mouse.
    private readonly DispatcherTimer _seekTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private TimeSpan _duration;
    private bool _canSeek;
    private bool _dragging;
    private TimeSpan? _seekHeld;      // where we just jumped to, shown until the app reports the new position
    private DateTime _seekHeldUntil;
    private bool _playing;

    private static readonly FontFamily Amiri = new(new Uri("pack://application:,,,/"), "./Fonts/#Amiri");

    public NowPlayingPanel(MediaService media)
    {
        InitializeComponent();
        _media = media;
        _onUpdated = state => Dispatcher.InvokeAsync(() => Apply(state));
        _media.Updated += _onUpdated;

        bool light = Theme.IsLight();
        Root.BorderBrush = new SolidColorBrush(light ? Color.FromArgb(0x20, 0, 0, 0) : Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
        Root.Background = _background;
        _background.Color = _palette.Background;
        Foreground = _palette.Foreground;
        DiscCover.Hole = _background;

        Apply(_media.Current);

        _seekTimer.Tick += (_, _) => UpdateSeekBar();
        Root.RenderTransform = _scale;

        Deactivated += (_, _) => ClosePanel();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) ClosePanel(); };
    }

    protected override void OnClosed(EventArgs e)
    {
        _media.Updated -= _onUpdated;
        _seekTimer.Stop();
        base.OnClosed(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        // No Alt+Tab entry.
        int ex = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_TOOLWINDOW);
    }

    /// <summary>Opens the panel over the island (<paramref name="anchor"/>, see Popups.Place) and focuses it so Esc / click-outside close it.</summary>
    internal void ShowAt(Native.RECT anchor, bool top, Size from)
    {
        // WPF's Show() only the first time; afterwards hide/show through Win32 (see QuickBar).
        _closing = false;
        Opacity = 0;
        if (!IsVisible) Show();
        ApplyStyle();
        ApplyShape();
        Popups.Place(this, _hwnd, anchor, top);
        IsShown = true;
        TitleText.Running = ArtistText.Running = true;
        RefreshSpin();
        UpdateSeekBar();
        _seekTimer.Start();
        Activate();

        // Grow out of the island: start at the capsule's size, pinned to the edge it hangs from, and ease out to full size.
        _from = from;
        _scale.CenterX = Root.ActualWidth / 2;
        _scale.CenterY = top ? 0 : Root.ActualHeight;
        Morph(1, 1, 1, null);
    }

    private readonly ScaleTransform _scale = new();
    private Size _from;
    private bool _closing;

    /// <summary>
    /// Opening (<paramref name="sx"/> &gt;= 1): the surface shows at once and grows out of the capsule
    /// (280ms, EaseMorph) while the content fades in behind it, so it is never seen squashed.
    /// Closing: the content fades first (80ms), then the surface shrinks back (180ms, EaseOut).
    /// With reduced motion only the fades run.
    /// </summary>
    private void Morph(double sx, double sy, double opacity, EventHandler? done)
    {
        bool opening = sx >= 1;
        bool reduced = Motion.Reduced;
        double nowX = _scale.ScaleX, nowY = _scale.ScaleY; // the animated values, before the animations are cleared
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        if (opening && Opacity == 0)
        {
            // Opening from closed: start at the capsule's size.
            _scale.ScaleX = reduced ? 1 : Ratio(_from.Width, Root.ActualWidth);
            _scale.ScaleY = reduced ? 1 : Ratio(_from.Height, Root.ActualHeight);
        }
        else
        {
            // Reopened while closing (or the reverse): carry on from where the scale is now.
            _scale.ScaleX = nowX;
            _scale.ScaleY = nowY;
        }

        var spline = opening ? Motion.EaseMorph : Motion.EaseOut;
        double ms = opening ? 280 : 180, delay = opening ? 0 : 60;
        DoubleAnimationUsingKeyFrames Shape(double to)
        {
            var a = Motion.To(to, ms, spline);
            a.BeginTime = TimeSpan.FromMilliseconds(delay);
            return a;
        }

        var x = Shape(reduced ? 1 : sx);
        if (done != null) x.Completed += done;
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, x);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, Shape(reduced ? 1 : sy));

        if (opening)
        {
            BeginAnimation(OpacityProperty, Motion.To(opacity, 60, Motion.EaseOut));
            var content = new DoubleAnimationUsingKeyFrames();
            double start = Opacity == 0 ? 0 : Inner.Opacity; // reopening mid-close keeps what is visible
            content.KeyFrames.Add(new DiscreteDoubleKeyFrame(start, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            content.KeyFrames.Add(new DiscreteDoubleKeyFrame(start, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));
            content.KeyFrames.Add(new SplineDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(240)), Motion.EaseOut));
            Inner.BeginAnimation(OpacityProperty, content);
        }
        else
        {
            Inner.BeginAnimation(OpacityProperty, Motion.To(0, 80, Motion.EaseOut));
            var fade = Motion.To(opacity, ms, Motion.EaseOut);
            fade.BeginTime = TimeSpan.FromMilliseconds(delay);
            BeginAnimation(OpacityProperty, fade);
        }
    }

    /// <summary>"Disc": spinning record whose ring is the seek bar. "Ribbon": cover strip, the artist over a large Amiri title, a line seek bar.</summary>
    private void ApplyStyle()
    {
        bool disc = Settings.Style == "Disc";
        DiscCover.Visibility = disc ? Visibility.Visible : Visibility.Collapsed;
        SquareCover.Visibility = disc ? Visibility.Collapsed : Visibility.Visible;
        SeekBar.Visibility = disc ? Visibility.Collapsed : Visibility.Visible; // the times stay; the ring does the seeking
        Grid.SetRow(TitleText, disc ? 0 : 1);
        Grid.SetRow(ArtistText, disc ? 1 : 0);
        TitleText.Margin = disc ? new Thickness(0) : new Thickness(0, 2, 0, 6);
        ArtistText.Margin = disc ? new Thickness(0, 4, 0, 0) : new Thickness(0);
        if (disc) TitleText.ClearValue(TextElement.FontFamilyProperty);
        else TitleText.SetValue(TextElement.FontFamilyProperty, Amiri);
        TitleText.SetValue(TextElement.FontSizeProperty, disc ? 17.0 : 30.0);
        TitleText.SetValue(TextElement.FontWeightProperty, disc ? FontWeights.SemiBold : FontWeights.Normal);
        // The large Ribbon title would otherwise widen the panel past the screen; capped, it scrolls instead.
        Root.MaxWidth = disc ? double.PositiveInfinity : 760;
    }

    /// <summary>The record spins only while the panel is open and something plays.</summary>
    private void RefreshSpin() => DiscCover.Spinning = IsShown && _playing && Settings.Style == "Disc";

    private static double Ratio(double part, double whole) => whole > 0 ? Math.Clamp(part / whole, 0.05, 1) : 1;

    /// <summary>Normal: 10px corners. Capsule: fully rounded ends, with wider side padding so the cover and controls stay clear of the curve.</summary>
    private void ApplyShape()
    {
        bool capsule = Settings.PanelShape == "Capsule";
        double side = capsule ? 45 : 20;
        Root.Padding = new Thickness(side, 20, side, 20);
        Root.Width = double.NaN; // size to content, so the seek times and buttons are never cut off
        UpdateLayout();
        Root.CornerRadius = new CornerRadius(capsule ? Root.ActualHeight / 2 : 10);
    }

    /// <summary>Whether the panel is currently on screen (WPF's IsVisible stays true once shown).</summary>
    public bool IsShown { get; private set; }

    /// <summary>Raised when the panel closes, so the island can come back.</summary>
    public event Action? Hidden;

    public void ClosePanel()
    {
        if (!IsShown || _closing) return;
        _closing = true;
        EndDrag();

        // Shrink back into the island, then hide; the island fades back in when Hidden fires.
        Morph(Ratio(_from.Width, Root.ActualWidth), Ratio(_from.Height, Root.ActualHeight), 0, (_, _) =>
        {
            if (!_closing) return; // reopened while shrinking
            _closing = false;
            IsShown = false;
            TitleText.Running = ArtistText.Running = false;
            RefreshSpin();
            _seekTimer.Stop();
            BeginAnimation(OpacityProperty, null);
            Opacity = 0;
            Native.ShowWindow(_hwnd, Native.SW_HIDE);
            Hidden?.Invoke();
        });
    }

    private MediaState? _lastState;

    /// <summary>Re-tints with the current accent mode (called when the setting changes).</summary>
    internal void RefreshAccent()
    {
        if (_lastState != null && Theme.PopupSource(_lastState.CoverColor) != _paletteSource) Apply(_lastState);
    }

    private void Apply(MediaState state)
    {
        TitleText.FadeTo(state.Title);
        ArtistText.FadeTo(state.Artist);
        Motion.CrossFade(CoverBrush, OldCoverBrush, OldCover, state.Cover);
        DiscCover.Cover = state.Cover;
        _playing = state.IsPlaying;
        RefreshSpin();

        _lastState = state;

        // Recolor (with a fade) only when the song's color actually changes.
        var source = Theme.PopupSource(state.CoverColor);
        if (source != _paletteSource)
        {
            _paletteSource = source;
            _palette = Palette.For(source);
            _palette.ApplyTo(this, _background);
        }

        Motion.SwapGlyph(PlayPauseButton, state.IsPlaying ? "\uF8AE" : "\uF5B0");
        PrevButton.IsEnabled = state.CanPrevious;
        PlayPauseButton.IsEnabled = state.CanPlayPause;
        NextButton.IsEnabled = state.CanNext;

        // Shuffle/repeat only work when the source app supports them.
        ShuffleButton.IsEnabled = state.CanShuffle;
        ShuffleButton.Foreground = state.CanShuffle && state.IsShuffle ? _palette.Active : _palette.Inactive;

        RepeatButton.IsEnabled = state.CanRepeat;
        RepeatButton.Content = state.Repeat == MediaPlaybackAutoRepeatMode.Track ? "\uE8ED" : "\uE8EE";
        RepeatButton.Foreground = state.CanRepeat && state.Repeat != MediaPlaybackAutoRepeatMode.None ? _palette.Active : _palette.Inactive;

        if (!state.HasSession) ClosePanel();
    }

    private void UpdateSeekBar()
    {
        if (_dragging) return;

        var timeline = _media.GetTimeline();
        if (timeline is not { } t)
        {
            // No length to show (no track, or a live stream): keep the bar's space but grey it out.
            _duration = TimeSpan.Zero;
            _canSeek = false;
            SeekArea.Opacity = 0.35;
            SeekBar.Cursor = null;
            DrawSeek(TimeSpan.Zero);
            DurationText.Text = "0:00";
            return;
        }

        _duration = t.Duration;
        _canSeek = t.CanSeek;
        SeekArea.Opacity = 1;
        SeekBar.Cursor = t.CanSeek ? Cursors.Hand : null;
        DurationText.Text = FormatTime(t.Duration);

        var position = t.Position;
        if (_seekHeld is { } held)
        {
            // Apps report the new position a little late; don't let the bar jump back meanwhile.
            if (DateTime.UtcNow < _seekHeldUntil && (position - held).Duration() > TimeSpan.FromSeconds(2))
                position = held;
            else
                _seekHeld = null;
        }
        DrawSeek(position);
    }

    private void DrawSeek(TimeSpan position)
    {
        double fraction = _duration > TimeSpan.Zero ? Math.Clamp(position / _duration, 0, 1) : 0;
        double width = SeekBar.ActualWidth;
        SeekFill.Width = width * fraction;
        Motion.Glide(DiscCover, Disc.ProgressProperty, fraction, _dragging ? 0 : _seekTimer.Interval.TotalMilliseconds, 1);
        SeekThumb.Margin = new Thickness(Math.Clamp(width * fraction - SeekThumb.Width / 2, 0, Math.Max(0, width - SeekThumb.Width)), 0, 0, 0);
        ElapsedText.Text = FormatTime(position);
    }

    private TimeSpan PositionAt(MouseEventArgs e)
    {
        if (Settings.Style == "Disc")
        {
            // Angle clockwise from 12 o'clock around the record's center = how far into the track.
            var p = e.GetPosition(DiscCover);
            double c = DiscCover.ActualWidth / 2;
            double turn = Math.Atan2(p.X - c, c - p.Y) / (2 * Math.PI);
            return _duration * (turn < 0 ? turn + 1 : turn);
        }
        double width = SeekBar.ActualWidth;
        double fraction = width > 0 ? Math.Clamp(e.GetPosition(SeekBar).X / width, 0, 1) : 0;
        return _duration * fraction;
    }

    private void Seek_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_canSeek || _duration <= TimeSpan.Zero) return;
        if (sender == DiscCover)
        {
            // Only the ring seeks; a click on the cover itself does nothing.
            if (!OnRing(e)) return;
        }
        _dragging = true;
        ((UIElement)sender).CaptureMouse();
        DrawSeek(PositionAt(e));
        e.Handled = true;
    }

    /// <summary>The transparent background makes the record's whole square clickable; this keeps it to a band around the ring.</summary>
    private bool OnRing(MouseEventArgs e)
    {
        var p = e.GetPosition(DiscCover);
        double c = DiscCover.ActualWidth / 2;
        double d = Math.Sqrt((p.X - c) * (p.X - c) + (p.Y - c) * (p.Y - c));
        return d >= c - DiscCover.RingThickness - DiscCover.RingGap - 6 && d <= c + 6;
    }

    private void Seek_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging) DrawSeek(PositionAt(e));
        if (sender == DiscCover) DiscCover.ShowThumb = _dragging || (_canSeek && OnRing(e));
    }

    private void Disc_MouseLeave(object sender, MouseEventArgs e) => DiscCover.ShowThumb = _dragging;

    private async void Seek_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        var target = PositionAt(e);
        EndDrag();
        DrawSeek(target);
        _seekHeld = target;
        _seekHeldUntil = DateTime.UtcNow.AddSeconds(2);
        await _media.SeekAsync(target);
    }

    private void EndDrag()
    {
        _dragging = false;
        DiscCover.ShowThumb = false;
        if (SeekBar.IsMouseCaptured) SeekBar.ReleaseMouseCapture();
        if (DiscCover.IsMouseCaptured) DiscCover.ReleaseMouseCapture();
    }

    private static string FormatTime(TimeSpan t) =>
        t.ToString(t.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);

    private async void Prev_Click(object sender, RoutedEventArgs e) => await _media.PreviousAsync();
    private async void PlayPause_Click(object sender, RoutedEventArgs e) => await _media.TogglePlayPauseAsync();
    private async void Next_Click(object sender, RoutedEventArgs e) => await _media.NextAsync();
    private async void Shuffle_Click(object sender, RoutedEventArgs e) => await _media.ToggleShuffleAsync();
    private async void Repeat_Click(object sender, RoutedEventArgs e) => await _media.CycleRepeatAsync();
}
