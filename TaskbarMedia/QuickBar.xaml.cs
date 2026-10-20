using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

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
        if (!IsShown)
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

        if (Opacity < 1)
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
    }

    /// <summary>Whether the bar is currently on screen (WPF's IsVisible stays true once shown).</summary>
    public bool IsShown { get; private set; }

    public void HideBar()
    {
        if (!IsShown) return;
        IsShown = false;
        TitleText.Running = ArtistText.Running = false;
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

        if (!state.HasSession) HideBar();
    }

    private async void Prev_Click(object sender, RoutedEventArgs e) => await _media.PreviousAsync();
    private async void PlayPause_Click(object sender, RoutedEventArgs e) => await _media.TogglePlayPauseAsync();
    private async void Next_Click(object sender, RoutedEventArgs e) => await _media.NextAsync();
}
