using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace TaskbarMedia;

/// <summary>
/// The island: a floating capsule with the cover, title and quick controls (see Island).
/// Never takes focus, so the app you are using stays active.
/// </summary>
public partial class QuickBar : Window
{
    private readonly MediaService _media;
    private readonly Action<MediaState> _onUpdated;

    /// <summary>Raised when the cover is clicked (opens the large panel).</summary>
    public event Action? CoverClicked;
    private readonly SolidColorBrush _background = new(Colors.Transparent);
    private Palette _palette = Palette.For(null);
    private Color? _paletteSource;
    private IntPtr _hwnd;
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _playing;

    public QuickBar(MediaService media)
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
        _progressTimer.Tick += (_, _) => UpdateProgress();

        ApplyStyle();
        Apply(_media.Current);
        CoverGrid.MouseLeftButtonUp += (_, _) => CoverClicked?.Invoke();
    }

    protected override void OnClosed(EventArgs e)
    {
        _media.Updated -= _onUpdated;
        base.OnClosed(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        // No activation (keeps focus in the user's app) and no Alt+Tab entry.
        int ex = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
    }

    /// <summary>
    /// Shows the capsule at <paramref name="anchor"/> (see Popups.Place), with fully rounded ends.
    /// </summary>
    internal void ShowAt(Native.RECT anchor, bool top)
    {
        // WPF's Show() only the first time; after that the window is hidden/shown directly
        // through Win32 so WPF doesn't redo its show/activation logic every time.
        if (!IsVisible)
        {
            Opacity = 0;
            Show();
        }
        if (!IsShown && Settings.Style == "Ribbon")
        {
            Root.CornerRadius = new CornerRadius(12);
            Root.Padding = new Thickness(10, 6, 10, 5);
        }
        else if (!IsShown)
        {
            // Pad the ends by half the height so the cover and buttons stay clear of the curve.
            Root.Padding = new Thickness(8, 6, 8, 6);
            UpdateLayout();
            double radius = Root.ActualHeight / 2;
            Root.CornerRadius = new CornerRadius(radius);
            Root.Padding = new Thickness(Math.Max(8, radius * 0.5), 6, Math.Max(8, radius * 0.5), 6);
            UpdateLayout();
        }

        Popups.Place(this, _hwnd, anchor, top);
        IsShown = true;
        TitleText.Running = ArtistText.Running = true;
        RefreshMotion();

        if (Opacity < 1)
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
    }

    /// <summary>Whether the bar is currently on screen (WPF's IsVisible stays true once shown).</summary>
    public bool IsShown { get; private set; }

    /// <summary>Hides the bar with a short fade (used while the large panel grows out of it).</summary>
    public void FadeOut()
    {
        if (!IsShown) return;
        IsShown = false;
        TitleText.Running = ArtistText.Running = false;
        RefreshMotion();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
        fade.Completed += (_, _) => { if (!IsShown) Native.ShowWindow(_hwnd, Native.SW_HIDE); };
        BeginAnimation(OpacityProperty, fade);
    }

    public void HideBar()
    {
        if (!IsShown) return;
        IsShown = false;
        TitleText.Running = ArtistText.Running = false;
        RefreshMotion();
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Native.ShowWindow(_hwnd, Native.SW_HIDE);
    }

    private MediaState? _lastState;

    /// <summary>Re-tints with the current accent mode (called when the setting changes).</summary>
    internal void RefreshAccent()
    {
        if (_lastState != null && Theme.PopupSource(_lastState.CoverColor) != _paletteSource) Apply(_lastState);
    }

    /// <summary>Switches between the "Disc" and "Ribbon" looks (Settings.Style); the caller shows the bar again.</summary>
    internal void ApplyStyle()
    {
        bool disc = Settings.Style == "Disc";
        DiscCover.Visibility = disc ? Visibility.Visible : Visibility.Collapsed;
        SquareCover.Visibility = ProgressLine.Visibility = disc ? Visibility.Collapsed : Visibility.Visible;
        ArtistText.Visibility = disc ? Visibility.Visible : Visibility.Collapsed;
        if (_lastState != null) Apply(_lastState);
        HideBar(); // shape and size are worked out again on the next show
    }

    /// <summary>Spinning and the progress timer run only while the bar is on screen and something plays.</summary>
    private void RefreshMotion()
    {
        bool live = IsShown && _playing;
        DiscCover.Spinning = live && Settings.Style == "Disc";
        if (live) _progressTimer.Start(); else _progressTimer.Stop();
        UpdateProgress();
    }

    private void UpdateProgress()
    {
        var t = _media.GetTimeline();
        double fraction = t is { } v ? v.Position / v.Duration : 0;
        DiscCover.Progress = fraction;
        ProgressFill.Width = ProgressLine.ActualWidth * Math.Clamp(fraction, 0, 1);
    }

    private void Apply(MediaState state)
    {
        bool ribbon = Settings.Style == "Ribbon";
        TitleText.Text = ribbon && state.Artist.Length > 0 && state.Title.Length > 0 ? state.Title + " — " + state.Artist : state.Title;
        ArtistText.Text = state.Artist;
        CoverBrush.ImageSource = state.Cover;
        DiscCover.Cover = state.Cover;
        Body.Opacity = state.IsPlaying ? 1 : 0.75;

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

        _playing = state.IsPlaying;
        RefreshMotion();
        if (!state.HasSession) HideBar();
    }

    private async void Prev_Click(object sender, RoutedEventArgs e) => await _media.PreviousAsync();
    private async void PlayPause_Click(object sender, RoutedEventArgs e) => await _media.TogglePlayPauseAsync();
    private async void Next_Click(object sender, RoutedEventArgs e) => await _media.NextAsync();
}
