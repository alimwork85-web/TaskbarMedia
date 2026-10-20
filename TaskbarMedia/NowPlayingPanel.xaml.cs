using System.Globalization;
using System.Windows;
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
        ApplyShape();
        Popups.Place(this, _hwnd, anchor, top);
        IsShown = true;
        TitleText.Running = ArtistText.Running = true;
        UpdateSeekBar();
        _seekTimer.Start();
        Activate();

        // Grow out of the island: start at the capsule's size, pinned to the edge it hangs from, and ease out to full size.
        _from = from;
        _scale.CenterX = Root.ActualWidth / 2;
        _scale.CenterY = top ? 0 : Root.ActualHeight;
        Morph(1, 1, 1, MorphOpen, null);
    }

    private static readonly Duration MorphOpen = TimeSpan.FromMilliseconds(320);
    private static readonly Duration MorphClose = TimeSpan.FromMilliseconds(220);
    private readonly ScaleTransform _scale = new();
    private Size _from;
    private bool _closing;

    /// <summary>Animates the panel's scale (from wherever it is now) and opacity together.</summary>
    private void Morph(double sx, double sy, double opacity, Duration duration, EventHandler? done)
    {
        var ease = new CubicEase { EasingMode = sx >= 1 ? EasingMode.EaseOut : EasingMode.EaseIn };
        if (sx >= 1 && Opacity == 0)
        {
            // Opening from closed: jump to the capsule's size first.
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _scale.ScaleX = Ratio(_from.Width, Root.ActualWidth);
            _scale.ScaleY = Ratio(_from.Height, Root.ActualHeight);
        }
        var x = new DoubleAnimation(sx, duration) { EasingFunction = ease };
        if (done != null) x.Completed += done;
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, x);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(sy, duration) { EasingFunction = ease });
        // The content fades a little faster than it moves, so it never looks squashed for long.
        BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, new Duration(duration.TimeSpan * 0.6)));
    }

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
        Morph(Ratio(_from.Width, Root.ActualWidth), Ratio(_from.Height, Root.ActualHeight), 0, MorphClose, (_, _) =>
        {
            if (!_closing) return; // reopened while shrinking
            _closing = false;
            IsShown = false;
            TitleText.Running = ArtistText.Running = false;
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
        TitleText.Text = state.Title;
        ArtistText.Text = state.Artist;
        CoverBrush.ImageSource = state.Cover;

        _lastState = state;

        // Recolor (with a fade) only when the song's color actually changes.
        var source = Theme.PopupSource(state.CoverColor);
        if (source != _paletteSource)
        {
            _paletteSource = source;
            _palette = Palette.For(source);
            _palette.ApplyTo(this, _background);
        }

        PlayPauseButton.Content = state.IsPlaying ? "\uE769" : "\uE768";
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
        SeekThumb.Margin = new Thickness(Math.Clamp(width * fraction - SeekThumb.Width / 2, 0, Math.Max(0, width - SeekThumb.Width)), 0, 0, 0);
        ElapsedText.Text = FormatTime(position);
    }

    private TimeSpan PositionAt(MouseEventArgs e)
    {
        double width = SeekBar.ActualWidth;
        double fraction = width > 0 ? Math.Clamp(e.GetPosition(SeekBar).X / width, 0, 1) : 0;
        return _duration * fraction;
    }

    private void Seek_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_canSeek || _duration <= TimeSpan.Zero) return;
        _dragging = true;
        SeekBar.CaptureMouse();
        DrawSeek(PositionAt(e));
        e.Handled = true;
    }

    private void Seek_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging) DrawSeek(PositionAt(e));
    }

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
        if (SeekBar.IsMouseCaptured) SeekBar.ReleaseMouseCapture();
    }

    private static string FormatTime(TimeSpan t) =>
        t.ToString(t.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);

    private async void Prev_Click(object sender, RoutedEventArgs e) => await _media.PreviousAsync();
    private async void PlayPause_Click(object sender, RoutedEventArgs e) => await _media.TogglePlayPauseAsync();
    private async void Next_Click(object sender, RoutedEventArgs e) => await _media.NextAsync();
    private async void Shuffle_Click(object sender, RoutedEventArgs e) => await _media.ToggleShuffleAsync();
    private async void Repeat_Click(object sender, RoutedEventArgs e) => await _media.CycleRepeatAsync();
}
