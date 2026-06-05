using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Web.WebView2.Core;

namespace FluxBrowser;

internal class DownloadInfo
{
    public string? Id;
    public CoreWebView2DownloadOperation? Operation;
    public Border? Border;
    public ProgressBar? ProgressBar;
    public Button? ActionBtn;
    public TextBlock? NameText;
    public TextBlock? StatusText;
}
