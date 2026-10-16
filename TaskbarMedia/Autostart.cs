using System.IO;
using Microsoft.Win32;

namespace TaskbarMedia;

/// <summary>Starts the app with Windows through the per-user Run key (no admin rights needed).</summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TaskbarMedia";

    /// <summary>
    /// Points the Run entry at the running EXE, so it follows the EXE if it is moved.
    /// Development builds (anything under a bin folder) are skipped so they never replace the real EXE.
    /// </summary>
    public static void Ensure()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            if (exe.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)) return;

            string command = $"\"{exe}\"";
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key.GetValue(ValueName) as string != command)
                key.SetValue(ValueName, command);
        }
        catch (Exception)
        {
            // Not being able to register autostart must never stop the app.
        }
    }
}
