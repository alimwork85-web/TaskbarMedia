using System.Windows.Interop;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace TaskbarMedia;

/// <summary>
/// The floating media island: the capsule (QuickBar) lives on its own near a screen edge, independent
/// of the taskbar, and opens into the large panel. A tray icon holds the settings and brings it back.
/// </summary>
internal sealed class Island : IDisposable
{
    private const int HotkeyId = 0x544D;

    private readonly MediaService _media;
    private readonly QuickBar _bar;
    private readonly NowPlayingPanel _panel;
    private readonly WinForms.NotifyIcon _tray;
    private readonly WinForms.ContextMenuStrip _menu = new() { RightToLeft = WinForms.RightToLeft.Yes };
    private bool _trayLight; // taskbar theme the tray icon was picked for
    private readonly IntPtr _hwnd; // the capsule's window; also receives the hotkey

    private readonly DispatcherTimer _guardTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _peekTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly DispatcherTimer _leaveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private bool _hasSession;
    private bool _fullscreen;
    private bool _toggled;      // tray click / Ctrl+Alt+M: hides it in "Always" mode, shows it in the others
    private bool _peek;         // "TrackChange" mode: shown for a few seconds after the song changes
    private string? _lastTrack; // null until the first track is seen, so startup does not count as a change
    private bool _hotkeyRegistered;

    public Island(MediaService media)
    {
        _media = media;
        _bar = new QuickBar(media);
        _panel = new NowPlayingPanel(media);
        _hwnd = new WindowInteropHelper(_bar).EnsureHandle();
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);

        _tray = new WinForms.NotifyIcon { Icon = LoadIcon(_trayLight = Theme.IsLight()), Text = "TaskbarMedia", ContextMenuStrip = _menu, Visible = true };
        _tray.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) Toggle(); };
        // Rebuild before the menu measures itself; rebuilding inside Opening left the island's menu laid out for the old items.
        _menu.Opened += (_, _) => MenuStyle.Round(_menu.Handle);
        _menu.Closed += (_, e) =>
        {
            if (!_panel.HoldOpen) return; // the tray's menu, or the panel wasn't open
            _panel.HoldOpen = false;
            if (!_panel.IsShown) return;
            // A click in another app closes the panel too, as it would have without the menu; otherwise focus goes
            // back to the panel so a later click elsewhere still closes it.
            if (e.CloseReason == WinForms.ToolStripDropDownCloseReason.AppFocusChange) _panel.ClosePanel();
            else
            {
                _panel.Activate();
                if (Settings.PanelOpen == "Hover" && !_panel.IsMouseOver) _leaveTimer.Start();
            }
        };
        _tray.MouseDown += (_, e) => { if (e.Button == WinForms.MouseButtons.Right) BuildMenu(); };

        _bar.CoverClicked += OpenPanel;
        // The settings menu is on the island itself (both sizes) as well as on the tray icon.
        _bar.MouseRightButtonUp += (_, e) => { e.Handled = true; ShowMenu(); };
        _panel.MouseRightButtonUp += (_, e) => { e.Handled = true; _leaveTimer.Stop(); ShowMenu(); };
        _bar.MouseEnter += (_, _) =>
        {
            if (Settings.PanelOpen == "Hover") _hoverTimer.Start();
        };
        _bar.MouseLeave += (_, _) =>
        {
            _hoverTimer.Stop();
            if (_peek) { _peekTimer.Stop(); _peekTimer.Start(); }
        };
        _hoverTimer.Tick += (_, _) => { _hoverTimer.Stop(); OpenPanel(); };

        // Opened by hovering: close again once the pointer has left it for a moment.
        _panel.MouseEnter += (_, _) => _leaveTimer.Stop();
        _panel.MouseLeave += (_, _) => { if (Settings.PanelOpen == "Hover") _leaveTimer.Start(); };
        _leaveTimer.Tick += (_, _) => { _leaveTimer.Stop(); if (!_panel.HoldOpen) _panel.ClosePanel(); }; // not under its menu
        _panel.Hidden += Update;

        _peekTimer.Tick += (_, _) =>
        {
            if (_bar.IsMouseOver) return; // keep it while the pointer is on it
            _peekTimer.Stop();
            _peek = false;
            Update();
        };

        _guardTimer.Tick += (_, _) =>
        {
            bool fullscreen = Settings.HideOnFullscreen && IsFullscreenAppRunning();
            if (fullscreen != _fullscreen)
            {
                _fullscreen = fullscreen;
                if (fullscreen) _panel.ClosePanel();
            }
            _bar.RefreshAccent(); // picks up a changed Windows accent color
            _panel.RefreshAccent();
            if (Theme.IsLight() != _trayLight)
            {
                var old = _tray.Icon;
                _tray.Icon = LoadIcon(_trayLight = !_trayLight);
                old?.Dispose();
            }
            Update(); // also re-places it after display or DPI changes
        };
        _guardTimer.Start();

        _media.Updated += state => _bar.Dispatcher.InvokeAsync(() => Apply(state));
        ApplyHotkey();
        Apply(_media.Current);
    }

    private void Apply(MediaState state)
    {
        _hasSession = state.HasSession;
        string tip = state.HasSession && state.Title.Length > 0 ? $"{state.Title} - {state.Artist}" : "TaskbarMedia";
        _tray.Text = tip.Length > 63 ? tip[..63] : tip; // the tray tooltip is limited to 63 characters

        if (state.HasSession && (state.Title.Length > 0 || state.Artist.Length > 0))
        {
            string track = state.Title + "|" + state.Artist;
            bool changed = _lastTrack != null && track != _lastTrack;
            _lastTrack = track;
            if (changed && Settings.IslandMode == "TrackChange")
            {
                _peek = true;
                _peekTimer.Stop();
                _peekTimer.Start();
            }
        }
        Update();
    }

    /// <summary>Shows or hides the capsule to match the current state and settings, at its chosen spot.</summary>
    private void Update()
    {
        bool wanted = Settings.IslandMode == "Always" ? !_toggled : _toggled || _peek;
        if (_hasSession && wanted && !_fullscreen && !_panel.IsShown)
            _bar.ShowAt(Anchor(), IsTop);
        else
            _bar.HideBar(instant: _fullscreen); // a full-screen app gets the screen at once
    }

    private static bool IsTop => Settings.Position.StartsWith("Top");

    /// <summary>The point the capsule hangs from (top positions) or rests on (bottom), inside the work area so it never covers the taskbar.</summary>
    private Native.RECT Anchor()
    {
        var origin = new Native.RECT { Left = 0, Top = 0, Right = 1, Bottom = 1 };
        var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        Native.GetMonitorInfo(Native.MonitorFromRect(ref origin, Native.MONITOR_DEFAULTTONEAREST), ref info);
        var work = info.rcWork;

        // Popups.Place centers on the anchor and keeps the window inside the work area, so an edge x pins it to that side.
        int x = Settings.Position.EndsWith("Left") ? work.Left : Settings.Position.EndsWith("Right") ? work.Right : (work.Left + work.Right) / 2;
        int y = IsTop ? work.Top : work.Bottom;
        return new Native.RECT { Left = x, Right = x, Top = y, Bottom = y };
    }

    private void OpenPanel()
    {
        _hoverTimer.Stop();
        if (_panel.IsShown || !Native.GetWindowRect(_hwnd, out var bar)) return;
        _panel.ShowAt(bar, IsTop, new System.Windows.Size(_bar.Root.ActualWidth, _bar.Root.ActualHeight));
        _bar.HideBar();
    }

    private void Toggle()
    {
        _panel.ClosePanel();
        _toggled = !_toggled;
        _peek = false;
        Update();
    }

    // QUNS_RUNNING_D3D_FULL_SCREEN / QUNS_PRESENTATION_MODE: Windows itself says a full-screen app is up.
    // QUNS_BUSY is also raised by transparent screen-sized overlays (Lightshot, Claude), so it only counts
    // when the foreground window really covers its monitor.
    private static bool IsFullscreenAppRunning() =>
        Native.SHQueryUserNotificationState(out int state) == 0 && (state is 3 or 4 || state == 2 && IsForegroundFullscreen());

    /// <summary>True when the foreground window (not the desktop or shell) covers its whole monitor.</summary>
    private static bool IsForegroundFullscreen()
    {
        IntPtr fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero || !Native.GetWindowRect(fg, out var r)) return false;

        var name = new System.Text.StringBuilder(64);
        Native.GetClassName(fg, name, name.Capacity);
        if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;

        var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(Native.MonitorFromWindow(fg, Native.MONITOR_DEFAULTTONEAREST), ref info)) return false;
        var m = info.rcMonitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }

    private void ShowMenu()
    {
        BuildMenu();
        _panel.HoldOpen = _panel.IsShown;
        _menu.Show(WinForms.Cursor.Position);
        // The island never takes focus, so without this the menu would not close on Esc or a click elsewhere.
        Native.SetForegroundWindow(_menu.Handle);
    }

    private void BuildMenu()
    {
        _menu.Items.Clear();
        _menu.Items.Add(Choice("الموضع", Settings.Position, v => { Settings.Position = v; Update(); },
            ("TopLeft", "أعلى اليسار"), ("TopCenter", "أعلى الوسط"), ("TopRight", "أعلى اليمين"),
            ("BottomLeft", "أسفل اليسار"), ("BottomCenter", "أسفل الوسط"), ("BottomRight", "أسفل اليمين")));
        _menu.Items.Add(Choice("الظهور", Settings.IslandMode, v => { Settings.IslandMode = v; _toggled = false; _peek = false; Update(); },
            ("Always", "دائمًا أثناء التشغيل"), ("TrackChange", "عند تغيير الأغنية فقط"), ("Manual", "يدويًا (الأيقونة أو Ctrl+Alt+M)")));
        _menu.Items.Add(Choice("فتح اللوحة الكبيرة", Settings.PanelOpen, v => Settings.PanelOpen = v,
            ("Hover", "بالتمرير فوق الشريط"), ("Click", "بالضغط على الغلاف")));
        _menu.Items.Add(Choice("التصميم", Settings.Style, v => { Settings.Style = v; _bar.ApplyStyle(); Update(); },
            ("Disc", "أسطوانة"), ("Ribbon", "شريط")));
        _menu.Items.Add(Choice("شكل اللوحة الكبيرة", Settings.PanelShape, v => Settings.PanelShape = v,
            ("Capsule", "كبسولة"), ("Normal", "عادي")));
        _menu.Items.Add(Choice("الألوان", Settings.AccentMode, v => { Settings.AccentMode = v; _bar.RefreshAccent(); _panel.RefreshAccent(); },
            ("Album", "من غلاف الأغنية"), ("Windows", "لون ويندوز"), ("Black", "أسود"), ("Glass", "زجاج")));
        _menu.Items.Add(Toggle("إخفاء في وضع ملء الشاشة", Settings.HideOnFullscreen, v => Settings.HideOnFullscreen = v));
        _menu.Items.Add(Toggle("اختصار Ctrl+Alt+M", Settings.HotkeyEnabled, v => { Settings.HotkeyEnabled = v; ApplyHotkey(); }));
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add(new WinForms.ToolStripMenuItem(_bar.IsShown || _panel.IsShown ? "إخفاء الشريط" : "إظهار الشريط", null, (_, _) => Toggle()));
        _menu.Items.Add(new WinForms.ToolStripMenuItem("خروج", null, (_, _) => ((App)System.Windows.Application.Current).Quit()));
        MenuStyle.Apply(_menu, Palette.For(Theme.PopupSource(_media.Current.CoverColor)));
    }

    private static WinForms.ToolStripMenuItem Choice(string title, string current, Action<string> set, params (string Key, string Text)[] options)
    {
        var menu = new WinForms.ToolStripMenuItem(title);
        foreach (var (key, text) in options)
            menu.DropDownItems.Add(new WinForms.ToolStripMenuItem(text, null, (_, _) => { set(key); Settings.Save(); }) { Checked = key == current });
        return menu;
    }

    private static WinForms.ToolStripMenuItem Toggle(string text, bool current, Action<bool> set) =>
        new(text, null, (_, _) => { set(!current); Settings.Save(); }) { Checked = current };

    /// <summary>Registers or releases the global Ctrl+Alt+M shortcut to match Settings.HotkeyEnabled.</summary>
    private void ApplyHotkey()
    {
        if (_hotkeyRegistered && !Settings.HotkeyEnabled)
        {
            Native.UnregisterHotKey(_hwnd, HotkeyId);
            _hotkeyRegistered = false;
        }
        else if (!_hotkeyRegistered && Settings.HotkeyEnabled)
        {
            _hotkeyRegistered = Native.RegisterHotKey(_hwnd, HotkeyId, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, Native.VK_M);
            if (!_hotkeyRegistered)
                System.Diagnostics.Debug.WriteLine($"Ctrl+Alt+M not registered (already taken?): error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Toggle();
            handled = true;
        }
        return IntPtr.Zero;
    }

    // The glyph alone, light on a dark taskbar and dark on a light one.
    private static System.Drawing.Icon LoadIcon(bool lightTaskbar)
    {
        var name = lightTaskbar ? "tray-dark.ico" : "tray-light.ico";
        using var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/" + name))!.Stream;
        return new System.Drawing.Icon(stream, WinForms.SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _guardTimer.Stop();
        _peekTimer.Stop();
        if (_hotkeyRegistered) Native.UnregisterHotKey(_hwnd, HotkeyId);
        _tray.Visible = false; // otherwise a dead icon stays in the tray until the mouse passes over it
        _tray.Dispose();
        _menu.Dispose();
        _bar.Close();
        _panel.Close();
    }
}
