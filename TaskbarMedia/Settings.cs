using System.IO;
using System.Text.Json;

namespace TaskbarMedia;

/// <summary>User options kept in %AppData%\TaskbarMedia\settings.json. Missing or damaged file = defaults.</summary>
internal static class Settings
{
    private static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarMedia");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public static bool HideOnFullscreen { get; set; } = true;
    public static bool HotkeyEnabled { get; set; } = true;
    /// <summary>"Album" or "Windows".</summary>
    public static string AccentMode { get; set; } = "Album";
    /// <summary>"TopLeft" | "TopCenter" | "TopRight" | "BottomLeft" | "BottomCenter" | "BottomRight".</summary>
    public static string Position { get; set; } = "TopCenter";
    /// <summary>When the island shows: "Always" (while something plays), "TrackChange" (a few seconds per new song) or "Manual" (tray icon / hotkey).</summary>
    public static string IslandMode { get; set; } = "Always";
    /// <summary>Large panel shape: "Normal" or "Capsule" (fully rounded ends).</summary>
    public static string PanelShape { get; set; } = "Normal";
    /// <summary>How the quick bar's cover opens the large panel: "Click" or "Hover".</summary>
    public static string PanelOpen { get; set; } = "Click";
    /// <summary>Look of the island and the large panel: "Disc" (spinning round cover) or "Ribbon" (square cover, progress line).</summary>
    public static string Style { get; set; } = "Disc";

    private sealed class Data
    {
        public bool HideOnFullscreen { get; set; } = true;
        public bool HotkeyEnabled { get; set; } = true;
        public string AccentMode { get; set; } = "Album";
        public string Position { get; set; } = "TopCenter";
        public string IslandMode { get; set; } = "Always";
        public string PanelShape { get; set; } = "Normal";
        public string PanelOpen { get; set; } = "Click";
        public string Style { get; set; } = "Disc";
    }

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var d = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath));
            if (d == null) return;
            HideOnFullscreen = d.HideOnFullscreen;
            HotkeyEnabled = d.HotkeyEnabled;
            AccentMode = d.AccentMode is "Windows" or "Black" or "Glass" ? d.AccentMode : "Album";
            PanelOpen = d.PanelOpen == "Hover" ? "Hover" : "Click";
            PanelShape = d.PanelShape == "Capsule" ? "Capsule" : "Normal";
            Style = d.Style == "Ribbon" ? "Ribbon" : "Disc";
            Position = d.Position is "TopLeft" or "TopCenter" or "TopRight" or "BottomLeft" or "BottomCenter" or "BottomRight" ? d.Position : "TopCenter";
            IslandMode = d.IslandMode is "TrackChange" or "Manual" ? d.IslandMode : "Always";
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
                HotkeyEnabled = HotkeyEnabled,
                AccentMode = AccentMode,
                Position = Position,
                IslandMode = IslandMode,
                PanelShape = PanelShape,
                PanelOpen = PanelOpen,
                Style = Style,
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
