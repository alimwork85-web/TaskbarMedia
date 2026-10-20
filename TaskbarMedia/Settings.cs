using System.IO;
using System.Text.Json;

namespace TaskbarMedia;

/// <summary>User options kept in %AppData%\TaskbarMedia\settings.json. Missing or damaged file = defaults.</summary>
internal static class Settings
{
    private static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarMedia");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public static bool HideOnFullscreen { get; set; } = true;
    public static bool AutoShowOnTrackChange { get; set; } = false;
    public static bool HotkeyEnabled { get; set; } = true;
    /// <summary>"Album" or "Windows".</summary>
    public static string AccentMode { get; set; } = "Album";
    /// <summary>"Auto" (follow the tray) | "TopLeft" | "TopRight" | "BottomLeft" | "BottomRight".</summary>
    public static string Position { get; set; } = "Auto";

    private sealed class Data
    {
        public bool HideOnFullscreen { get; set; } = true;
        public bool AutoShowOnTrackChange { get; set; } = false;
        public bool HotkeyEnabled { get; set; } = true;
        public string AccentMode { get; set; } = "Album";
        public string Position { get; set; } = "Auto";
    }

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var d = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath));
            if (d == null) return;
            HideOnFullscreen = d.HideOnFullscreen;
            AutoShowOnTrackChange = d.AutoShowOnTrackChange;
            HotkeyEnabled = d.HotkeyEnabled;
            AccentMode = d.AccentMode == "Windows" ? "Windows" : "Album";
            Position = d.Position is "TopLeft" or "TopRight" or "BottomLeft" or "BottomRight" ? d.Position : "Auto";
        }
        catch (Exception)
        {
            // Damaged file: keep the defaults.
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var d = new Data
            {
                HideOnFullscreen = HideOnFullscreen,
                AutoShowOnTrackChange = AutoShowOnTrackChange,
                HotkeyEnabled = HotkeyEnabled,
                AccentMode = AccentMode,
                Position = Position,
            };
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception)
        {
            // Saving is best-effort; the app keeps working with the in-memory values.
        }
    }
}
