using System.IO;

namespace FluxBrowser.Models;

public class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Default";
    public string Icon { get; set; } = "\uD83D\uDC64";
    public string Color { get; set; } = "#7c5fff";
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BuildBrowser", "Profiles", Id);

    public string WebView2DataDir => Path.Combine(DataDir, "WebView2");
    public string BookmarksFile => Path.Combine(DataDir, "bookmarks.json");
}
