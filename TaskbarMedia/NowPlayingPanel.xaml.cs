using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Windows.Media;

namespace TaskbarMedia;

/// <summary>
/// The large now-playing panel opened by clicking the cover in the quick bar.
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

        Deactivated += (_, _) => ClosePanel();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) ClosePanel(); };
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

        // No Alt+Tab entry.
        int ex = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_TOOLWINDOW);
    }

    /// <summary>Opens the panel above <paramref name="anchor"/> (screen px) and focuses it so Esc / click-outside close it.</summary>
    internal void ShowAbove(Native.RECT anchor)
    {
        // WPF's Show() only the first time; afterwards hide/show through Win32 (see QuickBar).
        Opacity = 0;
        if (!IsVisible) Show();
        UpdateLayout();
        Popups.PlaceAbove(this, _hwnd, anchor);
        IsShown = true;
        TitleText.Running = ArtistText.Running = true;
        Activate();
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
    }

    /// <summary>Whether the panel is currently on screen (WPF's IsVisible stays true once shown).</summary>
    public bool IsShown { get; private set; }

    public void ClosePanel()
    {
        if (!IsShown) return;
        IsShown = false;
        TitleText.Running = ArtistText.Running = false;
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Native.ShowWindow(_hwnd, Native.SW_HIDE);
    }

    private void Apply(MediaState state)
    {
        TitleText.Text = state.Title;
        ArtistText.Text = state.Artist;
        CoverBrush.ImageSource = state.Cover;

        // Recolor (with a fade) only when the song's color actually changes.
        if (state.CoverColor != _paletteSource)
        {
            _paletteSource = state.CoverColor;
            _palette = Palette.For(state.CoverColor);
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

    private async void Prev_Click(object sender, RoutedEventArgs e) => await _media.PreviousAsync();
    private async void PlayPause_Click(object sender, RoutedEventArgs e) => await _media.TogglePlayPauseAsync();
    private async void Next_Click(object sender, RoutedEventArgs e) => await _media.NextAsync();
    private async void Shuffle_Click(object sender, RoutedEventArgs e) => await _media.ToggleShuffleAsync();
    private async void Repeat_Click(object sender, RoutedEventArgs e) => await _media.CycleRepeatAsync();
}
