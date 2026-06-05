using System.IO;
using System.Text.Json;
using FluxBrowser.Models;

namespace FluxBrowser.Services;

public class ProfileManager
{
    private static readonly string ProfilesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FluxBrowser", "Profiles");

    private static readonly string ProfilesIndex = Path.Combine(ProfilesDir, "profiles.json");

    public List<Profile> Profiles { get; private set; } = [];
    public Profile Current { get; private set; } = new();

    public ProfileManager()
    {
        Load();
        if (Profiles.Count == 0)
        {
            var def = new Profile
            {
                Id = "default",
                Name = "Default",
                Icon = "\uD83C\uDF0D",
                Color = "#7c5fff"
            };
            Profiles.Add(def);
            Save();
        }
        Current = Profiles[0];
    }

    public Profile Add(string name, string icon = "\uD83D\uDC64", string color = "#7c5fff")
    {
        var p = new Profile
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Name = name,
            Icon = icon,
            Color = color
        };
        Profiles.Add(p);
        Save();
        return p;
    }

    public void Delete(string id)
    {
        if (Profiles.Count <= 1) return;
        Profiles.RemoveAll(p => p.Id == id);
        if (Current.Id == id)
            Current = Profiles[0];
        Save();
    }

    public Profile? Get(string id) =>
        Profiles.Find(p => p.Id == id);

    public void SwitchTo(string id)
    {
        var p = Get(id);
        if (p != null)
            Current = p;
    }

    private void Load()
    {
        try
        {
            if (!Directory.Exists(ProfilesDir))
                Directory.CreateDirectory(ProfilesDir);
            if (File.Exists(ProfilesIndex))
            {
                var json = File.ReadAllText(ProfilesIndex);
                Profiles = JsonSerializer.Deserialize<List<Profile>>(json) ?? [];
            }
        }
        catch { }
    }

    private void Save()
    {
        try
        {
            if (!Directory.Exists(ProfilesDir))
                Directory.CreateDirectory(ProfilesDir);
            var json = JsonSerializer.Serialize(Profiles, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ProfilesIndex, json);
        }
        catch { }
    }

    public HashSet<string> LoadBookmarks()
    {
        try
        {
            var file = Current.BookmarksFile;
            var dir = Path.GetDirectoryName(file);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            if (File.Exists(file))
            {
                var json = File.ReadAllText(file);
                return JsonSerializer.Deserialize<HashSet<string>>(json) ?? [];
            }
        }
        catch { }
        return [];
    }

    public void SaveBookmarks(HashSet<string> bookmarks)
    {
        try
        {
            var file = Current.BookmarksFile;
            var dir = Path.GetDirectoryName(file);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(bookmarks, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(file, json);
        }
        catch { }
    }
}
