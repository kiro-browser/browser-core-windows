using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;

namespace FluxBrowser.Models;

public class BrowserTab
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? CurrentUrl { get; set; }
    public string? PendingUrl { get; set; }
    public WebView2? WebView { get; set; }
    public Button? Button { get; set; }
    public TextBlock? TitleControl { get; set; }
    public bool IsLoading { get; set; }
}
