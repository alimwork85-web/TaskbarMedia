using System.Windows;

namespace TaskbarMedia;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private MediaService? _media;
    private Island? _island;

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
        Settings.Load();

        _media = new MediaService();
        _island = new Island(_media);
        await _media.StartAsync();
    }

    /// <summary>Exits the app (from the tray menu).</summary>
    public void Quit()
    {
        _island?.Dispose();
        _island = null;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _island?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
