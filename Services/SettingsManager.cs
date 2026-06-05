using System.IO;
using System.Text.Json;
using FluxBrowser.Models;

namespace FluxBrowser.Services;

public class SettingsManager
{
    public BrowserSettings Settings { get; private set; } = new();
    private readonly string _filePath;

    public SettingsManager(string profileDataDir)
    {
        _filePath = Path.Combine(profileDataDir, "settings.json");
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                Settings = JsonSerializer.Deserialize<BrowserSettings>(json) ?? new BrowserSettings();
            }
        }
        catch { Settings = new BrowserSettings(); }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch { }
    }
}
