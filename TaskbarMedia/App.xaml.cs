using System.Windows;
using System.Windows.Threading;

namespace TaskbarMedia;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private MediaService? _media;
    private TaskbarWidget? _widget;
    private bool _quitting;

    // After Explorer restarts, wait for the new taskbar (and give it a moment to settle) before re-creating the icon.
    private readonly DispatcherTimer _recreateTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _taskbarSeenTicks;

    // WPF does not report the icon's window being destroyed with the old taskbar, so check for it.
    private readonly DispatcherTimer _watchTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, "TaskbarMedia.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        Autostart.Ensure();

        _media = new MediaService();
        _recreateTimer.Tick += (_, _) => TryRecreateWidget();
        _watchTimer.Tick += (_, _) =>
        {
            if (_widget is { } w && w.Handle != IntPtr.Zero && !Native.IsWindow(w.Handle))
                OnWidgetLost(w);
        };
        CreateWidget();
        _watchTimer.Start();
        await _media.StartAsync();
    }

    private void CreateWidget()
    {
        var widget = new TaskbarWidget(_media!);
        widget.Closed += (_, _) => OnWidgetLost(widget);
        _widget = widget;
        widget.Show();
    }

    /// <summary>The icon is gone without the user choosing "exit": Explorer took the taskbar (and our icon) down.</summary>
    private void OnWidgetLost(TaskbarWidget widget)
    {
        if (_quitting || !ReferenceEquals(widget, _widget)) return;
        _widget = null;
        widget.Teardown();
        try { widget.Close(); } catch (Exception) { /* its window is already gone */ }

        _taskbarSeenTicks = 0;
        _recreateTimer.Start();
    }

    private void TryRecreateWidget()
    {
        if (Native.FindWindow("Shell_TrayWnd", null) == IntPtr.Zero)
        {
            _taskbarSeenTicks = 0;
            return;
        }

        if (++_taskbarSeenTicks < 2) return;
        _recreateTimer.Stop();
        CreateWidget();
    }

    /// <summary>Exits the app for real (as opposed to the icon being closed by an Explorer restart).</summary>
    public void Quit()
    {
        _quitting = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
