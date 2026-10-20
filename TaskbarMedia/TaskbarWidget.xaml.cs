using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Threading;

namespace TaskbarMedia;

/// <summary>
/// The cover icon that lives inside the taskbar, next to the tray (clock) area.
/// Visible while any media source exists (playing or paused); hidden when there is none.
/// </summary>
public partial class TaskbarWidget : Window
{
    private const int TrayGap = 4; // physical px between the icon and the tray area

    private readonly MediaService _media;
    private readonly Action<MediaState> _onUpdated;
    private IntPtr _hwnd;
    private IntPtr _taskbar;
    private (int Left, int Right)? _trayExtent; // screen px, from UI Automation
    private Native.RECT? _floatingFrame;        // visible taskbar when it floats (rounded, e.g. via Windhawk), screen px
    private volatile bool _trayQueryRunning;
    private readonly DispatcherTimer _trayTimer;
    private bool _hasSession;

    // Where UpdatePosition last put the icon (screen px). WPF's own layout can try to move the
    // window, so every move is forced back to this target in WM_WINDOWPOSCHANGING.
    private (int X, int Y, int Width, int Height)? _target;
    private int _targetScreenLeft;
    private bool _taskbarHidden;
    private string? _lastTrack;          // title + artist of the previous track; null until the first one is seen
    private readonly DispatcherTimer _autoCloseTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private bool _manualHidden;          // toggled by Ctrl+Alt+M
    private bool _hotkeyRegistered;
    private const int HotkeyId = 0x544D;
    private bool _fullscreenHidden;      // a full-screen window covers the monitor (Settings.HideOnFullscreen)

    // Checks often whether the icon drifted or an auto-hiding taskbar slid in/out, and follows right away.
    private readonly DispatcherTimer _guardTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    // Fades the icon out like a fading tray (e.g. a Windhawk mod): visible only while the mouse is
    // near the tray area or the icon, or while the quick bar / panel is open.
    private readonly DispatcherTimer _fadeTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool? _nearTray;
    private const int NearMargin = 12; // physical px around the tray + icon that still counts as "near"

    // Hover: a short delay before showing (ignores the mouse just passing by) and a grace
    // period before hiding (lets the mouse travel from the icon up to the bar).
    private readonly QuickBar _quickBar;
    private readonly NowPlayingPanel _panel;
    private readonly DispatcherTimer _showTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    private Brush _hoverBrush = Brushes.Transparent;

    public TaskbarWidget(MediaService media)
    {
        InitializeComponent();
        Settings.Load();
        HideFullscreenItem.IsChecked = Settings.HideOnFullscreen;
        AlwaysVisibleItem.IsChecked = Settings.AlwaysVisible;
        AutoShowItem.IsChecked = Settings.AutoShowOnTrackChange;
        HotkeyItem.IsChecked = Settings.HotkeyEnabled;
        ShapeNormalItem.IsChecked = Settings.PanelShape == "Normal";
        ShapeCapsuleItem.IsChecked = Settings.PanelShape == "Capsule";
        AccentAlbumItem.IsChecked = Settings.AccentMode == "Album";
        AccentWindowsItem.IsChecked = Settings.AccentMode == "Windows";
        foreach (System.Windows.Controls.MenuItem item in PositionMenu.Items) item.IsChecked = (string)item.Tag == Settings.Position;
        _media = media;
        _onUpdated = state => Dispatcher.InvokeAsync(() => Apply(state));
        _media.Updated += _onUpdated;

        ApplyTheme();
        Apply(_media.Current);

        SizeChanged += (_, _) => UpdatePosition();
        _quickBar = new QuickBar(media);
        _panel = new NowPlayingPanel(media);
        Root.MouseEnter += (_, _) =>
        {
            Root.Background = _hoverBrush;
            _hideTimer.Stop();
            if (!_quickBar.IsShown) _showTimer.Start();
        };
        Root.MouseLeave += (_, _) =>
        {
            Root.Background = Brushes.Transparent;
            _showTimer.Stop();
            _hideTimer.Start();
        };
        _quickBar.MouseEnter += (_, _) => _hideTimer.Stop();
        _quickBar.MouseLeave += (_, _) => _hideTimer.Start();
        _showTimer.Tick += (_, _) =>
        {
            _showTimer.Stop();
            if (_hasSession && !_panel.IsShown && Native.GetWindowRect(_hwnd, out var icon))
                _quickBar.ShowAbove(icon, IsRtl(), Settings.Position == "Auto" ? _floatingFrame : null);
        };

        // Clicking the cover in the quick bar swaps it for the large panel.
        _quickBar.CoverClicked += () =>
        {
            _hideTimer.Stop();
            _quickBar.HideBar();
            if (Native.GetWindowRect(_hwnd, out var icon))
                _panel.ShowAbove(icon);
        };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            _quickBar.HideBar();
        };

        // Tray icons come and go, and WPF's own layout can move the window after we place it,
        // so re-measure the tray and re-apply the position regularly (as FluentFlyout does).
        _trayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _trayTimer.Tick += (_, _) =>
        {
            // A new taskbar window (Explorer restarted but our window survived): attach to it.
            var current = Native.FindWindow("Shell_TrayWnd", null);
            if (current != IntPtr.Zero && current != _taskbar)
            {
                _trayExtent = null;
                EmbedInTaskbar();
            }
            UpdatePosition();
            RefreshTrayExtent();
            _quickBar.RefreshAccent(); // picks up a changed Windows accent color
            _panel.RefreshAccent();
        };
        _trayTimer.Start();

        _guardTimer.Tick += (_, _) =>
        {
            if (_target == null) return;

            // Hide while a full-screen window (video, game) covers our monitor; also closes the quick bar.
            bool fullscreen = Settings.HideOnFullscreen && IsForegroundFullscreen();
            if (fullscreen != _fullscreenHidden)
            {
                _fullscreenHidden = fullscreen;
                if (fullscreen) { _quickBar.HideBar(); _panel.ClosePanel(); }
                UpdatePosition();
                return;
            }

            // Follow an auto-hiding taskbar in and out quickly.
            if (Settings.Position == "Auto" && Native.GetWindowRect(_taskbar, out var bar) && IsTaskbarTuckedAway(bar) != _taskbarHidden)
            {
                UpdatePosition();
                return;
            }

            if (_hasSession && (Native.IsIconic(_hwnd) || IsBehindTaskbar() || (Native.GetWindowRect(_hwnd, out var now) && now.Left != _targetScreenLeft)))
                UpdatePosition();
        };
        _guardTimer.Start();

        _autoCloseTimer.Tick += (_, _) =>
        {
            _autoCloseTimer.Stop();
            // While the pointer is on the bar or the icon, the usual leave-to-hide handling takes over.
            if (!_quickBar.IsMouseOver && !Root.IsMouseOver) _quickBar.HideBar();
        };

        Opacity = 0;
        _fadeTimer.Tick += (_, _) => UpdateFade();
        _fadeTimer.Start();
    }

    private void UpdateFade()
    {
        bool near = Settings.AlwaysVisible || _quickBar.IsShown || _panel.IsShown || IsCursorNearTray();
        if (near == _nearTray) return;
        _nearTray = near;
        BeginAnimation(OpacityProperty, new DoubleAnimation(near ? 1 : 0, TimeSpan.FromMilliseconds(near ? 150 : 300)));
    }

    private bool IsCursorNearTray()
    {
        if (!Native.GetCursorPos(out var p)) return false;
        if (!Native.GetWindowRect(_taskbar, out var bar) || !Native.GetWindowRect(_hwnd, out var icon)) return false;

        // Manual corners: only the icon itself counts (the taskbar is somewhere else).
        if (Settings.Position != "Auto")
            return p.X >= icon.Left - NearMargin && p.X <= icon.Right + NearMargin
                && p.Y >= icon.Top - NearMargin && p.Y <= icon.Bottom + NearMargin;

        int left = icon.Left, right = icon.Right;
        if (_trayExtent is { } tray)
        {
            left = Math.Min(left, tray.Left);
            right = Math.Max(right, tray.Right);
        }
        return p.X >= left - NearMargin && p.X <= right + NearMargin
            && p.Y >= bar.Top - NearMargin && p.Y <= bar.Bottom;
    }

    /// <summary>The icon's window handle (zero until shown).</summary>
    public IntPtr Handle => _hwnd;

    /// <summary>
    /// Stops everything and closes the quick bar and panel. Called on close, and by App when
    /// Explorer restarts: destroying the taskbar destroys the windows it owns, and WPF
    /// does not raise Closed in that case.
    /// </summary>
    public void Teardown()
    {
        if (_tornDown) return;
        _tornDown = true;
        _media.Updated -= _onUpdated;
        _trayTimer.Stop();
        _guardTimer.Stop();
        _showTimer.Stop();
        _hideTimer.Stop();
        _fadeTimer.Stop();
        _autoCloseTimer.Stop();
        if (_hotkeyRegistered) { Native.UnregisterHotKey(_hwnd, HotkeyId); _hotkeyRegistered = false; }
        _quickBar.Close();
        _panel.Close();
    }
    private bool _tornDown;

    protected override void OnClosed(EventArgs e)
    {
        Teardown();
        base.OnClosed(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);

        // WPF read the minimum window size once at creation (before our hook existed);
        // ask again so it picks up the lowered minimum from WndProc, then re-measure.
        IntPtr mmi = Marshal.AllocHGlobal(40);
        try
        {
            // Zero everything, but leave the maximum sizes (ptMaxSize, ptMaxTrackSize) unbounded.
            for (int i = 0; i < 40; i += 4) Marshal.WriteInt32(mmi, i, 0);
            foreach (int offset in new[] { 8, 12, 32, 36 }) Marshal.WriteInt32(mmi, offset, 32767);
            Native.SendMessage(_hwnd, Native.WM_GETMINMAXINFO, IntPtr.Zero, mmi);
        }
        finally { Marshal.FreeHGlobal(mmi); }
        SizeToContent = SizeToContent.Manual;
        SizeToContent = SizeToContent.WidthAndHeight;

        ApplyHotkey();
        EmbedInTaskbar();
    }

    /// <summary>
    /// Makes the primary taskbar (Shell_TrayWnd) this window's owner. An owned window always stays
    /// above its owner, so the icon sits on top of the taskbar while remaining a normal top-level
    /// window. (A child window inside the taskbar flickered on right-to-left Windows: the mirrored
    /// taskbar flipped it to the opposite side every time WPF redrew it.)
    /// </summary>
    private void EmbedInTaskbar()
    {
        _taskbar = Native.FindWindow("Shell_TrayWnd", null);
        if (_taskbar == IntPtr.Zero) return;

        Native.SetWindowLongPtr(_hwnd, Native.GWLP_HWNDPARENT, _taskbar);
        int ex = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);

        UpdatePosition();
        RefreshTrayExtent();

        // Once WPF has finished its first layout pass, place the icon again.
        Dispatcher.InvokeAsync(UpdatePosition, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>
    /// Places the icon on the inner side of the tray (clock) area, vertically centered on the taskbar.
    /// Hidden while there is no media, or while an auto-hiding taskbar is tucked away.
    /// </summary>
    private void UpdatePosition()
    {
        if (_hwnd == IntPtr.Zero || _taskbar == IntPtr.Zero) return;
        if (!Native.GetWindowRect(_taskbar, out var bar)) return;

        double scale = Native.GetDpiForWindow(_taskbar) / 96.0;
        if (scale <= 0) return;

        int width = (int)Math.Ceiling(ActualWidth * scale);
        int height = (int)Math.Ceiling(ActualHeight * scale);
        int barHeight = bar.Bottom - bar.Top;

        bool rtl = IsRtl();

        // Screen-space left edge of the icon.
        int left = rtl ? bar.Left + 20 : bar.Right - 20 - width;
        if (_trayExtent is { } extent)
        {
            left = rtl ? extent.Right + TrayGap : extent.Left - TrayGap - width;
        }
        else
        {
            // Fallback until UI Automation answers (TrayNotifyWnd is narrower than the real tray on Windows 11).
            IntPtr tray = Native.FindWindowEx(_taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            if (tray != IntPtr.Zero && Native.GetWindowRect(tray, out var trayRect) && trayRect.Right > trayRect.Left)
                left = rtl ? trayRect.Right + TrayGap : trayRect.Left - TrayGap - width;
        }

        // Screen coordinates (this is a top-level window, so no mirroring is involved).
        int x = Math.Max(bar.Left, Math.Min(left, bar.Right - width));
        int y = bar.Top + (barHeight - height) / 2;

        _taskbarHidden = IsTaskbarTuckedAway(bar);
        if (Settings.Position != "Auto")
        {
            // Manual corner of the primary monitor, 8 dip from the edges.
            var origin = new Native.RECT { Left = 0, Top = 0, Right = 1, Bottom = 1 };
            var mon = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
            if (Native.GetMonitorInfo(Native.MonitorFromRect(ref origin, Native.MONITOR_DEFAULTTONEAREST), ref mon))
            {
                int margin = (int)Math.Round(8 * scale);
                x = Settings.Position.EndsWith("Left") ? mon.rcMonitor.Left + margin
                    : Settings.Position.EndsWith("Center") ? (mon.rcMonitor.Left + mon.rcMonitor.Right - width) / 2
                    : mon.rcMonitor.Right - margin - width;
                y = Settings.Position.StartsWith("Top") ? mon.rcMonitor.Top + margin : mon.rcMonitor.Bottom - margin - height;
                _taskbarHidden = false;
            }
        }
        _target = (x, y, width, height);
        _targetScreenLeft = x;
        uint showFlag = _hasSession && !_taskbarHidden && !_fullscreenHidden && !_manualHidden ? Native.SWP_SHOWWINDOW : Native.SWP_HIDEWINDOW;

        // If something minimized the icon (e.g. a tool that hides windows), restore it first.
        if (Native.IsIconic(_hwnd))
            Native.ShowWindow(_hwnd, Native.SW_SHOWNOACTIVATE);

        // The taskbar is topmost, and owning the icon does not always keep it above (e.g. after a
        // reboot it ended up hidden behind the taskbar), so the icon has to be topmost too.
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, width, height, Native.SWP_NOACTIVATE | showFlag);
    }

    /// <summary>True when the foreground window (not the desktop or shell) exactly covers the monitor the icon is on.</summary>
    private bool IsForegroundFullscreen()
    {
        IntPtr fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == _hwnd || fg == _taskbar || !Native.GetWindowRect(fg, out var r)) return false;

        var name = new System.Text.StringBuilder(64);
        Native.GetClassName(fg, name, name.Capacity);
        if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;

        var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(Native.MonitorFromWindow(_hwnd, Native.MONITOR_DEFAULTTONEAREST), ref info)) return false;
        var m = info.rcMonitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }

    /// <summary>True when the taskbar has come in front of the icon (it re-asserts topmost now and then).</summary>
    private bool IsBehindTaskbar()
    {
        for (IntPtr h = Native.GetWindow(_hwnd, Native.GW_HWNDPREV); h != IntPtr.Zero; h = Native.GetWindow(h, Native.GW_HWNDPREV))
            if (h == _taskbar) return true;
        return false;
    }

    /// <summary>True when an auto-hiding taskbar has slid (almost) off the bottom of its monitor.</summary>
    private static bool IsTaskbarTuckedAway(Native.RECT bar)
    {
        var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(Native.MonitorFromRect(ref bar, Native.MONITOR_DEFAULTTONEAREST), ref info))
            return false;
        return bar.Top >= info.rcMonitor.Bottom - 8 || bar.Bottom <= info.rcMonitor.Top + 8;
    }

    /// <summary>
    /// Asks UI Automation for the real screen extent of the tray buttons (clock, icons, chevron).
    /// Runs off the UI thread so a slow Explorer can never block the icon.
    /// </summary>
    private void RefreshTrayExtent()
    {
        if (_trayQueryRunning || _taskbar == IntPtr.Zero) return;
        _trayQueryRunning = true;
        var taskbar = _taskbar;

        Task.Run(() => (Tray: QueryTrayExtent(taskbar), Frame: QueryFloatingFrame(taskbar))).ContinueWith(t =>
        {
            _trayQueryRunning = false;
            _floatingFrame = t.Result.Frame;
            if (t.Result.Tray is { } extent && extent != _trayExtent)
            {
                _trayExtent = extent;
                Dispatcher.InvokeAsync(UpdatePosition);
            }
        });
    }

    private static (int Left, int Right)? QueryTrayExtent(IntPtr taskbar)
    {
        try
        {
            var root = AutomationElement.FromHandle(taskbar);
            var condition = new OrCondition(
                new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTrayIcon"),
                new PropertyCondition(AutomationElement.AutomationIdProperty, "NotifyItemIcon"));

            int left = int.MaxValue, right = int.MinValue;
            foreach (AutomationElement item in root.FindAll(TreeScope.Descendants, condition))
            {
                var r = item.Current.BoundingRectangle;
                if (r.IsEmpty || r.Width <= 0) continue;
                left = Math.Min(left, (int)r.Left);
                right = Math.Max(right, (int)r.Right);
            }
            return left < right ? (left, right) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The visible taskbar ("TaskbarFrame") when it is floating, i.e. clearly narrower than the
    /// taskbar window (a styled, rounded taskbar). Null for the normal full-width taskbar.
    /// </summary>
    private static Native.RECT? QueryFloatingFrame(IntPtr taskbar)
    {
        try
        {
            if (!Native.GetWindowRect(taskbar, out var bar)) return null;
            var frame = AutomationElement.FromHandle(taskbar).FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "TaskbarFrame"));
            var r = frame?.Current.BoundingRectangle;
            if (r is not { IsEmpty: false } rect || rect.Width <= 0) return null;
            if (rect.Width > (bar.Right - bar.Left) - 40) return null;
            return new Native.RECT { Left = (int)rect.Left, Top = (int)rect.Top, Right = (int)rect.Right, Bottom = (int)rect.Bottom };
        }
        catch (Exception)
        {
            return null;
        }
    }

    // On right-to-left Windows (e.g. Arabic) the taskbar is mirrored: the tray/clock sits on the left.
    private bool IsRtl() =>
        (Native.GetWindowLong(_taskbar, Native.GWL_EXSTYLE) & Native.WS_EX_LAYOUTRTL) != 0;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Native.WM_HOTKEY when wParam.ToInt32() == HotkeyId:
                _manualHidden = !_manualHidden;
                if (_manualHidden) { _quickBar.HideBar(); _panel.ClosePanel(); }
                UpdatePosition();
                handled = true;
                return IntPtr.Zero;

            // Swallowed (as FluentFlyout does) so shell mods that enumerate taskbar
            // child windows can't freeze on our window.
            case Native.WM_WINDOWPOSCHANGING:
                if (_target is { } t)
                {
                    int flags = Marshal.ReadInt32(lParam, Native.WINDOWPOS_Flags);
                    if ((flags & Native.SWP_NOMOVE) == 0)
                    {
                        Marshal.WriteInt32(lParam, Native.WINDOWPOS_X, t.X);
                        Marshal.WriteInt32(lParam, Native.WINDOWPOS_Y, t.Y);
                    }
                    if ((flags & Native.SWP_NOSIZE) == 0)
                    {
                        Marshal.WriteInt32(lParam, Native.WINDOWPOS_Cx, t.Width);
                        Marshal.WriteInt32(lParam, Native.WINDOWPOS_Cy, t.Height);
                    }
                }
                handled = true;
                return IntPtr.Zero;

            case Native.WM_GETOBJECT:
            case Native.WM_SHOWWINDOW:
            case Native.WM_NCCALCSIZE:
            case Native.WM_IME_SETCONTEXT:
            case Native.WM_IME_NOTIFY:
                handled = true;
                return IntPtr.Zero;

            // Windows gives every window a minimum width (SM_CXMINTRACK, ~132 DIP) and WPF
            // enforces it, which made the 40 DIP icon window 198 px wide and fight our sizing.
            // Lower the minimum before WPF reads it.
            case Native.WM_GETMINMAXINFO:
                Marshal.WriteInt32(lParam, Native.MINMAXINFO_MinTrackX, 1);
                Marshal.WriteInt32(lParam, Native.MINMAXINFO_MinTrackY, 1);
                break;

            case Native.WM_DPICHANGED:
            case Native.WM_DPICHANGED_AFTERPARENT:
                Dispatcher.InvokeAsync(() =>
                {
                    InvalidateMeasure();
                    UpdateLayout();
                    UpdatePosition();
                    RefreshTrayExtent();
                }, DispatcherPriority.Loaded);
                break;
        }
        return IntPtr.Zero;
    }

    private void Apply(MediaState state)
    {
        CoverBrush.ImageSource = state.Cover;

        if (state.HasSession != _hasSession)
        {
            _hasSession = state.HasSession;
            UpdatePosition();
        }

        // Peek at the quick bar when the track really changes (not the first one seen at startup).
        if (state.HasSession && (state.Title.Length > 0 || state.Artist.Length > 0))
        {
            string track = state.Title + "|" + state.Artist;
            bool changed = _lastTrack != null && track != _lastTrack;
            _lastTrack = track;
            if (changed) AutoShowQuickBar();
        }
    }

    private void AutoShowQuickBar()
    {
        if (!Settings.AutoShowOnTrackChange || _panel.IsShown || _fullscreenHidden || _manualHidden) return;
        if (!Native.GetWindowRect(_hwnd, out var icon)) return;
        _quickBar.ShowAbove(icon, IsRtl(), Settings.Position == "Auto" ? _floatingFrame : null);
        _autoCloseTimer.Stop();
        _autoCloseTimer.Start();
    }

    private void ApplyTheme()
    {
        bool light = Theme.IsLight();

        Foreground = light ? Brushes.Black : Brushes.White;
        _hoverBrush = new SolidColorBrush(light ? Color.FromArgb(0x18, 0, 0, 0) : Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
    }

    private void AlwaysVisible_Click(object sender, RoutedEventArgs e) { Settings.AlwaysVisible = AlwaysVisibleItem.IsChecked; Settings.Save(); }
    private void HideFullscreen_Click(object sender, RoutedEventArgs e) { Settings.HideOnFullscreen = HideFullscreenItem.IsChecked; Settings.Save(); }
    private void AutoShow_Click(object sender, RoutedEventArgs e) { Settings.AutoShowOnTrackChange = AutoShowItem.IsChecked; Settings.Save(); }
    private void Hotkey_Click(object sender, RoutedEventArgs e) { Settings.HotkeyEnabled = HotkeyItem.IsChecked; Settings.Save(); ApplyHotkey(); }

    /// <summary>Registers or releases the global Ctrl+Alt+M shortcut to match Settings.HotkeyEnabled.</summary>
    private void ApplyHotkey()
    {
        if (_hwnd == IntPtr.Zero) return;
        if (_hotkeyRegistered && !Settings.HotkeyEnabled)
        {
            Native.UnregisterHotKey(_hwnd, HotkeyId);
            _hotkeyRegistered = false;
            _manualHidden = false; // without the shortcut there would be no way to bring the icon back
            UpdatePosition();
        }
        else if (!_hotkeyRegistered && Settings.HotkeyEnabled)
        {
            _hotkeyRegistered = Native.RegisterHotKey(_hwnd, HotkeyId, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, Native.VK_M);
            if (!_hotkeyRegistered)
                System.Diagnostics.Debug.WriteLine($"Ctrl+Alt+M not registered (already taken?): error {Marshal.GetLastWin32Error()}");
        }
    }

    private void AccentAlbum_Click(object sender, RoutedEventArgs e) => SetAccent("Album");
    private void AccentWindows_Click(object sender, RoutedEventArgs e) => SetAccent("Windows");

    private void Position_Click(object sender, RoutedEventArgs e)
    {
        Settings.Position = (string)((System.Windows.Controls.MenuItem)sender).Tag;
        foreach (System.Windows.Controls.MenuItem item in PositionMenu.Items) item.IsChecked = (string)item.Tag == Settings.Position;
        Settings.Save();
        UpdatePosition();
    }

    private void ShapeNormal_Click(object sender, RoutedEventArgs e) => SetShape("Normal");
    private void ShapeCapsule_Click(object sender, RoutedEventArgs e) => SetShape("Capsule");

    private void SetShape(string shape)
    {
        Settings.PanelShape = shape;
        ShapeNormalItem.IsChecked = shape == "Normal";
        ShapeCapsuleItem.IsChecked = shape == "Capsule";
        Settings.Save();
    }

    private void SetAccent(string mode)
    {
        Settings.AccentMode = mode;
        AccentAlbumItem.IsChecked = mode == "Album";
        AccentWindowsItem.IsChecked = mode == "Windows";
        Settings.Save();
        _quickBar.RefreshAccent();
        _panel.RefreshAccent();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).Quit();
}
