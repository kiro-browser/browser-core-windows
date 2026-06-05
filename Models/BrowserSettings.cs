namespace FluxBrowser.Models;

public class BrowserSettings
{
    public string DefaultSearchEngine { get; set; } = "duckduckgo";
    public string HomePage { get; set; } = "https://duckduckgo.com";
    public string StartupBehavior { get; set; } = "home";
    public bool PromptBeforeDownload { get; set; } = false;
    public bool DarkMode { get; set; } = true;

    public string GetSearchUrl(string query)
    {
        var encoded = Uri.EscapeDataString(query);
        return DefaultSearchEngine switch
        {
            "google" => $"https://www.google.com/search?q={encoded}",
            "bing" => $"https://www.bing.com/search?q={encoded}",
            "yahoo" => $"https://search.yahoo.com/search?p={encoded}",
            "brave" => $"https://search.brave.com/search?q={encoded}",
            _ => $"https://duckduckgo.com/?q={encoded}",
        };
    }

    public bool IsSearchUrl(string url)
    {
        var domain = DefaultSearchEngine switch
        {
            "google" => "google.com",
            "bing" => "bing.com",
            "yahoo" => "search.yahoo.com",
            "brave" => "search.brave.com",
            _ => "duckduckgo.com",
        };
        return url.Contains(domain + "/search") || url.Contains(domain + "/?q=");
    }

    public static readonly Dictionary<string, string> SearchEngineNames = new()
    {
        ["duckduckgo"] = "DuckDuckGo",
        ["google"] = "Google",
        ["bing"] = "Bing",
        ["yahoo"] = "Yahoo",
        ["brave"] = "Brave",
    };

    public static readonly Dictionary<string, string> StartupBehaviorNames = new()
    {
        ["home"] = "Home page",
        ["newtab"] = "New tab page",
        ["restore"] = "Restore last session",
    };
}
