using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using FluxBrowser.Models;
using FluxBrowser.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FluxBrowser;

public partial class MainWindow : Window
{
    private static readonly string DuckDuckGoUrl = "https://duckduckgo.com";
    private static readonly string DownloadsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    private static readonly string FixedRuntimePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FluxBrowser", "WebView2Runtime",
        "Microsoft.WebView2.FixedVersionRuntime.149.0.4022.52.x86");

    private readonly ProfileManager _profiles = new();
    private readonly List<BrowserTab> _tabs = [];
    private BrowserTab? _currentTab;
    private int _tabCounter;
    private bool _isNavigatingProgrammatically;
    private bool _isMaximized;
    private bool _isFullscreen;
    private WindowState _savedWindowState;
    private HashSet<string> _bookmarkedUrls = [];
    private List<HistoryEntry> _history = [];
    private string _lastFindText = "";
    private Services.SettingsManager? _settingsManager;
    private readonly List<DownloadInfo> _downloads = [];

    private string CurrentHomePage => _settingsManager?.Settings.HomePage ?? DuckDuckGoUrl;

    public MainWindow()
    {
        InitializeComponent();
        _settingsManager = new Services.SettingsManager(_profiles.Current.DataDir);
        LoadProfileBookmarks();
        LoadHistory();
        InitProfileSelector();
        InitializeStartup();
    }

    private void InitializeStartup()
    {
        var behavior = _settingsManager?.Settings.StartupBehavior ?? "home";
        switch (behavior)
        {
            case "restore":
                if (_history.Count > 0)
                {
                    var urls = _history.Take(10).Select(h => h.Url).ToList();
                    AddNewTab(urls[0], true);
                    for (int i = 1; i < urls.Count; i++)
                        AddNewTab(urls[i], false);
                    return;
                }
                goto default;
            case "newtab":
                AddNewTab("about:blank", true);
                return;
            default:
                AddNewTab(CurrentHomePage, true);
                return;
        }
    }

    // -----------------------------------------------------
    //  Profile Management
    // -----------------------------------------------------

    private void InitProfileSelector()
    {
        ProfileSelector.SelectionChanged -= ProfileSelector_SelectionChanged;
        ProfileSelector.ItemsSource = _profiles.Profiles;
        ProfileSelector.SelectedItem = _profiles.Current;
        ProfileSelector.SelectionChanged += ProfileSelector_SelectionChanged;
    }

    private void ProfileSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;
        var profile = (Profile)e.AddedItems[0]!;
        if (profile.Id == _profiles.Current.Id) return;

        SaveProfileBookmarks();
        SaveHistory();
        _profiles.SwitchTo(profile.Id);
        _settingsManager = new Services.SettingsManager(_profiles.Current.DataDir);
        RebuildTabs(CurrentHomePage);
        LoadProfileBookmarks();
        LoadHistory();
        UpdateBookmarkStar("");
    }

    private void RebuildTabs(string url)
    {
        foreach (var tab in _tabs.ToList())
        {
            tab.WebView?.Dispose();
            if (tab.Button != null) TabPanel.Children.Remove(tab.Button);
        }
        _tabs.Clear();
        _currentTab = null;
        _tabCounter = 0;
        AddNewTab(url, true);
    }

    private void AddProfile()
    {
        var dialog = new ProfileDialog();
        if (dialog.ShowDialog() == true)
        {
            var profile = _profiles.Add(dialog.ProfileName, dialog.ProfileIcon, dialog.ProfileColor);
            ProfileSelector.ItemsSource = null;
            ProfileSelector.ItemsSource = _profiles.Profiles;
            ProfileSelector.SelectedItem = profile;
        }
    }

    private void DeleteCurrentProfile()
    {
        if (_profiles.Current.Id == "default")
        { StatusText.Text = "Cannot delete default profile"; return; }
        if (_profiles.Profiles.Count <= 1) return;

        _profiles.Delete(_profiles.Current.Id);
        ProfileSelector.ItemsSource = null;
        ProfileSelector.ItemsSource = _profiles.Profiles;
        ProfileSelector.SelectedItem = _profiles.Current;
        RebuildTabs(CurrentHomePage);
        LoadProfileBookmarks();
    }

    // -----------------------------------------------------
    //  Tab Management
    // -----------------------------------------------------

    private async void AddNewTab(string? url = null, bool select = true)
    {
        _tabCounter++;
        var tab = new BrowserTab { Id = _tabCounter, PendingUrl = url ?? CurrentHomePage };

        var tabBtn = CreateTabButton(tab);
        var webView = new WebView2 { Visibility = Visibility.Collapsed };

        var userDataDir = _profiles.Current.WebView2DataDir;
        if (Directory.Exists(FixedRuntimePath))
        {
            webView.CreationProperties = new CoreWebView2CreationProperties
            {
                BrowserExecutableFolder = FixedRuntimePath,
                UserDataFolder = userDataDir
            };
        }

        TabPanel.Children.Add(tabBtn);
        ContentArea.Children.Add(webView);

        tab.Button = tabBtn;
        tab.WebView = webView;
        _tabs.Add(tab);

        try
        {
            await webView.EnsureCoreWebView2Async();

            webView.CoreWebView2.Settings.IsScriptEnabled = true;
            webView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = true;
            webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = true;
            webView.CoreWebView2.Settings.IsGeneralAutofillEnabled = true;

            webView.CoreWebView2.NewWindowRequested += (s, e) =>
            { e.Handled = true; AddNewTab(e.Uri, true); };

            webView.CoreWebView2.DownloadStarting += (s, e) => OnDownloadStarting(e);

            webView.CoreWebView2.ContextMenuRequested += (s, e) =>
            {
                e.Handled = true;
                ShowWebViewContextMenu(e, tab);
            };

            webView.NavigationStarting += (s, e) => OnNavigationStarting(tab, e);
            webView.NavigationCompleted += (s, e) => OnNavigationCompleted(tab, e);
            webView.CoreWebView2.SourceChanged += (s, e) => OnSourceChanged(tab);
            webView.CoreWebView2.HistoryChanged += (s, e) => OnHistoryChanged(tab);
            webView.CoreWebView2.FaviconChanged += (_, _) =>
                Dispatcher.Invoke(() => UpdateTabFavicon(tab));
        }
        catch
        {
            ShowWebViewError(webView);
        }

        if (select) SelectTab(tab);
        NavigateTo(tab, tab.PendingUrl ?? CurrentHomePage);
    }

    private Button CreateTabButton(BrowserTab tab)
    {
        var btn = new Button
        {
            Style = (Style)FindResource("BrowserTab"),
            Width = 200,
            Content = null
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var favicon = new System.Windows.Controls.Image
        {
            Width = 14, Height = 14,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };

        var title = new TextBlock
        {
            Text = "New Tab", FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)FindResource("TextSecondaryBrush")
        };

        var closeBtn = new Button
        {
            Content = "\u2715", FontSize = 8, Width = 18, Height = 18,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Close tab", Opacity = 0
        };
        closeBtn.Click += (_, _) => CloseTab(tab);

        // Show close button on hover
        btn.MouseEnter += (_, _) => closeBtn.Opacity = 1;
        btn.MouseLeave += (_, _) => closeBtn.Opacity = 0;

        Grid.SetColumn(favicon, 0);
        grid.Children.Add(favicon);

        var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(title);
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);

        Grid.SetColumn(closeBtn, 2);
        grid.Children.Add(closeBtn);

        btn.Content = grid;

        btn.Click += (_, _) => SelectTab(tab);
        btn.MouseDown += (s, e) =>
        { if (e.ChangedButton == MouseButton.Middle) { e.Handled = true; CloseTab(tab); } };

        tab.TitleControl = title;
        tab.FaviconImage = favicon;
        tab.CloseButton = closeBtn;
        return btn;
    }

    private async void UpdateTabFavicon(BrowserTab tab)
    {
        try
        {
            if (tab.WebView?.CoreWebView2 == null || tab.FaviconImage == null) return;
            using var stream = await tab.WebView.CoreWebView2.GetFaviconAsync(
                Microsoft.Web.WebView2.Core.CoreWebView2FaviconImageFormat.Png);
            if (stream == null || stream.Length == 0) return;
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = stream;
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            tab.FaviconImage.Source = bitmap;
            tab.FaviconImage.Visibility = Visibility.Visible;
        }
        catch { }
    }

    private void SelectTab(BrowserTab tab)
    {
        if (_currentTab == tab) return;

        if (_currentTab != null)
        {
            _currentTab.WebView!.Visibility = Visibility.Collapsed;
            _currentTab.Button!.Background = (Brush)FindResource("BgSurfaceBrush");
            _currentTab.Button.Foreground = (Brush)FindResource("TextSecondaryBrush");
            if (_currentTab.TitleControl != null)
                _currentTab.TitleControl.Foreground = (Brush)FindResource("TextSecondaryBrush");
        }

        _currentTab = tab;
        tab.WebView!.Visibility = Visibility.Visible;
        tab.Button!.Background = (Brush)FindResource("BgBaseBrush");
        tab.Button.Foreground = (Brush)FindResource("TextPrimaryBrush");
        if (tab.TitleControl != null)
            tab.TitleControl.Foreground = (Brush)FindResource("TextPrimaryBrush");

        UpdateUrlBar(tab.CurrentUrl ?? "about:blank");
        UpdateNavButtons(tab);
        UpdateSecurityInfo(tab);
        UpdateBookmarkStar(tab.CurrentUrl ?? "");
        UpdateTitle(tab);
        UpdateZoomDisplay();
    }

    private void CloseTab(BrowserTab tab)   // Takes in the tab to close;
                                            // from the click event.
    {
        // If only one tab left, close the window.
        if (_tabs.Count <= 1) { Close(); return; }

        var idx = _tabs.IndexOf(tab);   // Get index before removing
        tab.WebView?.Dispose();         // Dispose WebView to release resources and file locks
        if (tab.Button != null) TabPanel.Children.Remove(tab.Button);   // Remove tab button from UI
        _tabs.Remove(tab);  // Remove from list

        // If the closed tab is the current one,
        // switch to the nearest tab.
        if (_currentTab == tab)
            SelectTab(_tabs[Math.Min(idx, _tabs.Count - 1)]);
    }

    // -----------------------------------------------------
    //  Navigation
    // -----------------------------------------------------

    private static bool IsUrl(string input)
    {
        input = input.Trim();
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https")) return true;
        if (Uri.TryCreate("https://" + input, UriKind.Absolute, out uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https") && input.Contains('.')) return true;
        return false;
    }

    private string NormalizeUrl(string input)
    {
        input = input.Trim();
        if (string.IsNullOrWhiteSpace(input)) return CurrentHomePage;
        if (input.StartsWith("about:")) return CurrentHomePage;
        if (IsUrl(input))
        {
            if (!input.StartsWith("http://") && !input.StartsWith("https://")) return "https://" + input;
            return input;
        }
        return _settingsManager?.Settings.GetSearchUrl(input) ??
               $"https://duckduckgo.com/?q={Uri.EscapeDataString(input)}";
    }

    private void NavigateTo(BrowserTab tab, string url)
    {
        if (tab.WebView?.CoreWebView2 == null) { tab.PendingUrl = url; return; }
        _isNavigatingProgrammatically = true;
        try { tab.WebView.CoreWebView2.Navigate(url); }
        finally { _isNavigatingProgrammatically = false; }
    }

    private void NavigateCurrent(string url)
    {
        if (_currentTab != null) NavigateTo(_currentTab, url);
    }

    private void OnNavigationStarting(BrowserTab tab, CoreWebView2NavigationStartingEventArgs e)
    {
        tab.IsLoading = true;
        if (tab != _currentTab) return;
        Dispatcher.Invoke(() =>
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            LoadProgress.Visibility = Visibility.Visible;
            StatusText.Text = "Loading...";
            var sb = new Storyboard();
            var anim = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(1))) { RepeatBehavior = RepeatBehavior.Forever };
            Storyboard.SetTarget(anim, SpinnerRotate);
            Storyboard.SetTargetProperty(anim, new PropertyPath("Angle"));
            sb.Children.Add(anim);
            sb.Begin();
        });
    }

    private void OnNavigationCompleted(BrowserTab tab, CoreWebView2NavigationCompletedEventArgs e)
    {
        tab.IsLoading = false;
        Dispatcher.Invoke(() =>
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            LoadProgress.Visibility = Visibility.Collapsed;
            LoadProgress.Value = 0;
            if (tab == _currentTab)
            {
                StatusText.Text = e.IsSuccess ? "Done" : "Page load error";
                UpdateNavButtons(tab);
                UpdateSecurityInfo(tab);
            }
        });
    }

    private void OnSourceChanged(BrowserTab tab)
    {
        if (tab.WebView?.CoreWebView2?.Source == null) return;
        var url = tab.WebView.CoreWebView2.Source;
        tab.CurrentUrl = url;

        Dispatcher.Invoke(() =>
        {
            var title = tab.WebView?.CoreWebView2?.DocumentTitle ?? "Flux Browser";
            tab.Title = title;
            if (tab.TitleControl != null)
                tab.TitleControl.Text = string.IsNullOrEmpty(title) ? "New Tab" : title;

            if (tab == _currentTab)
            {
                if (!_isNavigatingProgrammatically) UpdateUrlBar(url);
                UpdateTitle(tab);
                UpdateBookmarkStar(url);
            }

            // Add to history
            var skipSearch = _settingsManager?.Settings.IsSearchUrl(url) ?? url.Contains("duckduckgo.com/?q=");
            if (!url.StartsWith("about:") && !skipSearch)
            {
                _history.RemoveAll(h => h.Url == url);
                _history.Insert(0, new HistoryEntry { Url = url, Title = title, VisitedAt = DateTime.Now });
                SaveHistory();
            }
        });
    }

    private void OnHistoryChanged(BrowserTab tab)
    {
        Dispatcher.Invoke(() => { if (tab == _currentTab) UpdateNavButtons(tab); });
    }

    // -----------------------------------------------------
    //  History
    // -----------------------------------------------------

    private void LoadHistory()
    {
        try
        {
            var file = _profiles.Current.BookmarksFile.Replace("bookmarks.json", "history.json");
            if (File.Exists(file))
                _history = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(file)) ?? [];
        }
        catch { _history = []; }
    }

    private void SaveHistory()
    {
        try
        {
            var file = _profiles.Current.BookmarksFile.Replace("bookmarks.json", "history.json");
            var dir = Path.GetDirectoryName(file);
            if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(file, JsonSerializer.Serialize(_history));
        }
        catch { }
    }

    private void ShowHistoryManager()
    {
        if (_currentTab == null) return;
        var items = string.Join("", _history.Select(h =>
            $"<li style='padding:8px; margin:4px 0; background:#1c1c36; border-radius:6px;'>" +
            $"<small style='color:#585878;'>{h.VisitedAt:MMM dd HH:mm}</small> " +
            $"<a href='{h.Url}' style='color:#818cf8; text-decoration:none;'>{h.Title ?? h.Url}</a></li>"));

        var html = $"<html><body style='background:#0d0d1a; color:#f1f1f6; font-family:system-ui; padding:2rem;'>" +
                   $"<div style='display:flex; justify-content:space-between; align-items:center;'>" +
                   $"<h2 style='color:#6366f1; margin:0;'>History ({_profiles.Current.Name})</h2>" +
                   $"<button onclick='fetch(\"flux://clear-history\")' style='background:#ef4444; color:white; border:none; padding:6px 14px; border-radius:6px; cursor:pointer;'>Clear All</button>" +
                   $"</div><ul style='list-style:none; padding:0;'>{items}</ul></body></html>";
        _currentTab.WebView?.NavigateToString(html);
    }

    // -----------------------------------------------------
    //  Zoom
    // -----------------------------------------------------

    private void ZoomIn()
    {
        if (_currentTab?.WebView == null) return;
        var factor = Math.Round(_currentTab.WebView.ZoomFactor + 0.1, 1);
        if (factor > 3.0) factor = 3.0;
        _currentTab.WebView.ZoomFactor = factor;
        UpdateZoomDisplay();
    }

    private void ZoomOut()
    {
        if (_currentTab?.WebView == null) return;
        var factor = Math.Round(_currentTab.WebView.ZoomFactor - 0.1, 1);
        if (factor < 0.3) factor = 0.3;
        _currentTab.WebView.ZoomFactor = factor;
        UpdateZoomDisplay();
    }

    private void ZoomReset()
    {
        if (_currentTab?.WebView == null) return;
        _currentTab.WebView.ZoomFactor = 1.0;
        UpdateZoomDisplay();
    }

    private void UpdateZoomDisplay()
    {
        if (_currentTab?.WebView == null) return;
        ZoomText.Text = $"{(int)(_currentTab.WebView.ZoomFactor * 100)}%";
    }

    // -----------------------------------------------------
    //  Downloads Manager
    // -----------------------------------------------------

    private void OnDownloadStarting(CoreWebView2DownloadStartingEventArgs e)
    {
        var op = e.DownloadOperation;
        var fileName = Path.GetFileName(new Uri(op.Uri).AbsolutePath);
        if (string.IsNullOrEmpty(fileName)) fileName = "download_" + DateTime.Now.Ticks;
        e.ResultFilePath = GetUniqueFilePath(Path.Combine(DownloadsPath, fileName));

        if (_settingsManager?.Settings.PromptBeforeDownload == true)
        {
            var proceed = false;
            Dispatcher.Invoke(() =>
            {
                proceed = MessageBox.Show(
                    $"Do you want to download this file?\n\n{fileName}",
                    "Download", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            });
            if (!proceed) { e.Cancel = true; return; }
        }

        var info = new DownloadInfo
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Operation = op,
        };
        _downloads.Add(info);
        Dispatcher.Invoke(() => AddDownloadUI(info));

        op.StateChanged += (_, _) => Dispatcher.Invoke(() => UpdateDownloadState(info));
        op.BytesReceivedChanged += (_, _) => Dispatcher.Invoke(() => UpdateDownloadProgress(info));
    }

    private void AddDownloadUI(DownloadInfo info)
    {
        DownloadsBar.Visibility = Visibility.Visible;

        var border = new Border
        {
            Background = (Brush)FindResource("BgElevatedBrush"),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 2, 0, 2),
            Padding = new Thickness(10, 6, 10, 6)
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameText = new TextBlock
        {
            Text = Path.GetFileName(info.Operation?.ResultFilePath ?? "file"),
            FontSize = 12,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        grid.Children.Add(nameText);

        var statusText = new TextBlock
        {
            Text = "Starting...",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 2, 0, 0)
        };
        Grid.SetRow(statusText, 1);
        grid.Children.Add(statusText);

        var actionBtn = new Button
        {
            Content = "Cancel",
            Style = (Style)FindResource("ToolbarButton"),
            Width = 60, Height = 24, FontSize = 10,
            Tag = info.Id
        };
        actionBtn.Click += (_, _) => CancelDownload(info.Id!);
        Grid.SetColumn(actionBtn, 1);
        Grid.SetRowSpan(actionBtn, 2);
        grid.Children.Add(actionBtn);

        var progressBar = new ProgressBar
        {
            Style = (Style)FindResource("ModernProgressBar"),
            Height = 3,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Margin = new Thickness(0, 6, 0, 0)
        };
        Grid.SetRow(progressBar, 1);
        grid.Children.Add(progressBar);

        border.Child = grid;
        DownloadsPanel.Children.Insert(0, border);

        info.Border = border;
        info.ProgressBar = progressBar;
        info.ActionBtn = actionBtn;
        info.NameText = nameText;
        info.StatusText = statusText;

        UpdateDownloadProgress(info);
        UpdateDownloadState(info);
    }

    private void UpdateDownloadProgress(DownloadInfo info)
    {
        if (info.Operation == null || info.ProgressBar == null || info.StatusText == null) return;
        var received = info.Operation.BytesReceived;
        var total = (long)info.Operation.TotalBytesToReceive;
        info.ProgressBar.Value = total > 0 ? (double)received / total * 100 : 0;
        info.StatusText.Text = total > 0
            ? $"{FormatSize(received)} / {FormatSize(total)}"
            : $"{FormatSize(received)}";
    }

    private void UpdateDownloadState(DownloadInfo info)
    {
        if (info.Operation == null || info.ActionBtn == null) return;
        var state = info.Operation.State;
        switch (state)
        {
            case CoreWebView2DownloadState.Completed:
                info.ActionBtn.Content = "Open";
                info.ActionBtn.Click -= (_, _) => CancelDownload(info.Id!);
                info.ActionBtn.Click += (_, _) => OpenDownloadFile(info.Operation.ResultFilePath);
                if (info.StatusText != null)
                    info.StatusText.Text = "Completed � " + FormatSize(info.Operation.BytesReceived);
                break;
            case CoreWebView2DownloadState.Interrupted:
                info.ActionBtn.Content = "Retry";
                info.ActionBtn.Click -= (_, _) => CancelDownload(info.Id!);
                info.ActionBtn.Click += (_, _) => RetryDownload(info);
                if (info.StatusText != null)
                    info.StatusText.Text = "Failed";
                break;
        }
        ClearDownloadsBtn.Visibility = _downloads.Any(d =>
        {
            try { return d.Operation?.State == CoreWebView2DownloadState.Completed; }
            catch { return false; }
        }) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CancelDownload(string id)
    {
        var info = _downloads.Find(d => d.Id == id);
        try { info?.Operation?.Cancel(); } catch { }
    }

    private void OpenDownloadFile(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { }
    }

    private void ShowDownloadInFolder(string path)
    {
        try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); }
        catch { }
    }

    private void RetryDownload(DownloadInfo info)
    {
        // Re-create the download UI and re-trigger
        if (info.NameText != null && info.Operation != null)
        {
            var url = info.Operation.Uri;
            if (info.Border != null) DownloadsPanel.Children.Remove(info.Border);
            _downloads.Remove(info);
            // Navigate to the URL again to trigger download
            if (_currentTab?.WebView?.CoreWebView2 != null)
                _currentTab.WebView.CoreWebView2.Navigate(url);
        }
    }

    private void ClearDownloads_Click(object sender, RoutedEventArgs e)
    {
        var completed = _downloads.Where(d =>
        {
            try { return d.Operation?.State == CoreWebView2DownloadState.Completed; }
            catch { return false; }
        }).ToList();
        foreach (var d in completed)
        {
            if (d.Border != null) DownloadsPanel.Children.Remove(d.Border);
            _downloads.Remove(d);
        }
        if (_downloads.Count == 0 && DownloadsBar.Visibility == Visibility.Visible)
            CloseDownloadsBar();
        ClearDownloadsBtn.Visibility = Visibility.Collapsed;
    }

    private void CloseDownloads_Click(object sender, RoutedEventArgs e) => CloseDownloadsBar();

    private void CloseDownloadsBar()
    {
        DownloadsBar.Visibility = Visibility.Collapsed;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }

    // -----------------------------------------------------
    //  Find in Page
    // -----------------------------------------------------

    private void FindInPage()
    {
        if (_currentTab?.WebView?.CoreWebView2 == null) return;
        FindBar.Visibility = Visibility.Visible;
        FindBox.Focus();
        FindBox.SelectAll();
    }

    private void FindBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { FindNext(); e.Handled = true; }
        if (e.Key == Key.Escape) { FindClose(); e.Handled = true; }
        if (e.Key == Key.F3 && Keyboard.IsKeyDown(Key.LeftShift) == false) { FindNext(); e.Handled = true; }
        if (e.Key == Key.F3 && Keyboard.IsKeyDown(Key.LeftShift)) { FindPrev(); e.Handled = true; }
    }

    private async void FindNext()
    {
        if (_currentTab?.WebView?.CoreWebView2 == null) return;
        var text = FindBox.Text;
        if (string.IsNullOrEmpty(text)) return;
        _lastFindText = text;
        var result = await _currentTab.WebView.CoreWebView2.ExecuteScriptAsync(
            $"window.find('{EscapeJs(text)}', false, false, true, false, true)");
        UpdateFindResult(result == "true");
    }

    private async void FindPrev()
    {
        if (_currentTab?.WebView?.CoreWebView2 == null) return;
        var text = FindBox.Text;
        if (string.IsNullOrEmpty(text)) return;
        _lastFindText = text;
        var result = await _currentTab.WebView.CoreWebView2.ExecuteScriptAsync(
            $"window.find('{EscapeJs(text)}', false, true, true, false, true)");
        UpdateFindResult(result == "true");
    }

    private void UpdateFindResult(bool found)
    {
        FindResultText.Text = found ? "" : "Not found";
        FindResultText.Foreground = found
            ? (Brush)FindResource("TextMutedBrush")
            : (Brush)FindResource("DangerBrush");
    }

    private void FindClose()
    {
        FindBar.Visibility = Visibility.Collapsed;
        FindBox.Text = "";
        FindResultText.Text = "";
        FocusWebView();
    }

    private void FindPrev_Click(object sender, RoutedEventArgs e) => FindPrev();
    private void FindNext_Click(object sender, RoutedEventArgs e) => FindNext();
    private void FindClose_Click(object sender, RoutedEventArgs e) => FindClose();

    private static string EscapeJs(string s) =>
        s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");

    // -----------------------------------------------------
    //  Fullscreen
    // -----------------------------------------------------

    private void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;
        if (_isFullscreen)
        {
            _savedWindowState = WindowState;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            WindowChrome.SetWindowChrome(this, null);
            TitleBar.Visibility = Visibility.Collapsed;
            FindBar.Visibility = Visibility.Collapsed;
        }
        else
        {
            WindowStyle = WindowStyle.None;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0, ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            });
            WindowState = _savedWindowState;
            _isMaximized = _savedWindowState == WindowState.Maximized;
            TitleBar.Visibility = Visibility.Visible;
        }
    }

    // -----------------------------------------------------
    //  UI Updates
    // -----------------------------------------------------

    private void UpdateUrlBar(string url)
    {
        UrlBox.Text = url == "about:blank" ? "" : url;
        UrlBox.ToolTip = url;
    }

    private void UpdateNavButtons(BrowserTab tab)
    {
        if (tab.WebView?.CoreWebView2 == null) return;
        try
        {
            BackBtn.IsEnabled = tab.WebView.CoreWebView2.CanGoBack;
            ForwardBtn.IsEnabled = tab.WebView.CoreWebView2.CanGoForward;
        }
        catch { }
    }

    private void UpdateSecurityInfo(BrowserTab tab)
    {
        var url = tab.CurrentUrl ?? "";
        if (url.StartsWith("https://"))
        {
            SecurityIcon.Text = "\uD83D\uDD12";
            SecurityBadge.ToolTip = "Secure (HTTPS)";
            SecurityBadge.Background = new SolidColorBrush(Color.FromRgb(45, 80, 45));
        }
        else
        {
            SecurityIcon.Text = "\u26A0\uFE0F";
            SecurityBadge.ToolTip = "Not secure";
            SecurityBadge.Background = new SolidColorBrush(Color.FromRgb(80, 45, 45));
        }
    }

    private void UpdateTitle(BrowserTab tab)
    {
        var title = tab.Title ?? "Flux Browser";
        WindowTitle.Text = title;
        Title = $"{title} - Flux Browser";
    }

    private void UpdateBookmarkStar(string url)
    {
        if (string.IsNullOrEmpty(url) || url == CurrentHomePage || url == "about:blank")
        { BookmarkIcon.Text = "\u2606"; BookmarkBtn.Foreground = (Brush)FindResource("TextSecondaryBrush"); return; }
        BookmarkIcon.Text = _bookmarkedUrls.Contains(url) ? "\u2605" : "\u2606";
        BookmarkBtn.Foreground = _bookmarkedUrls.Contains(url)
            ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("TextSecondaryBrush");
    }

    private void ShowWebViewError(WebView2 webView)
    {
        var tb = new TextBlock
        {
            Text = "WebView2 runtime is not installed.\nPlease install Microsoft Edge WebView2 Runtime.",
            Foreground = (Brush)FindResource("TextSecondaryBrush"), FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center
        };
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(tb);
        webView.Visibility = Visibility.Visible;
    }

    private void FocusWebView()
    {
        if (_currentTab?.WebView != null)
            _currentTab.WebView.Focus();
    }

    // -----------------------------------------------------
    //  Bookmarks (per-profile)
    // -----------------------------------------------------

    private void ToggleBookmark()
    {
        var url = _currentTab?.CurrentUrl;
        if (string.IsNullOrEmpty(url) || url == CurrentHomePage || url == "about:blank") return;
        if (_bookmarkedUrls.Contains(url)) _bookmarkedUrls.Remove(url);
        else _bookmarkedUrls.Add(url);
        SaveProfileBookmarks();
        UpdateBookmarkStar(url);
    }

    private void LoadProfileBookmarks() => _bookmarkedUrls = _profiles.LoadBookmarks();
    private void SaveProfileBookmarks() => _profiles.SaveBookmarks(_bookmarkedUrls);

    private void ShowBookmarksManager()
    {
        if (_currentTab == null) return;
        var items = string.Join("", _bookmarkedUrls.Select(url =>
            $"<li style='padding:10px; margin:6px 0; background:#1c1c36; border-radius:6px;'>" +
            $"<a href='{url}' style='color:#818cf8; text-decoration:none;'>{url}</a></li>"));
        _currentTab.WebView?.NavigateToString(
            $"<html><body style='background:#0d0d1a; color:#f1f1f6; font-family:system-ui; padding:2rem;'>" +
            $"<h2 style='color:#6366f1;'>Bookmarks ({_profiles.Current.Name})</h2>" +
            $"<ul style='list-style:none; padding:0;'>{items}</ul></body></html>");
    }

    // -----------------------------------------------------
    //  Context Menu
    // -----------------------------------------------------

    private void ShowWebViewContextMenu(CoreWebView2ContextMenuRequestedEventArgs e, BrowserTab tab)
    {
        var menu = new ContextMenu();
        menu.Background = new SolidColorBrush(Color.FromRgb(20, 20, 40));
        menu.BorderBrush = new SolidColorBrush(Color.FromRgb(46, 46, 82));
        menu.Foreground = new SolidColorBrush(Color.FromRgb(241, 241, 246));

        var back = new MenuItem { Header = "Back", IsEnabled = tab.WebView?.CoreWebView2?.CanGoBack ?? false };
        back.Click += (_, _) => { try { tab.WebView?.GoBack(); } catch { } };
        menu.Items.Add(back);

        var fwd = new MenuItem { Header = "Forward", IsEnabled = tab.WebView?.CoreWebView2?.CanGoForward ?? false };
        fwd.Click += (_, _) => { try { tab.WebView?.GoForward(); } catch { } };
        menu.Items.Add(fwd);

        menu.Items.Add(new Separator());

        var reload = new MenuItem { Header = "Reload" };
        reload.Click += (_, _) => { try { tab.WebView?.Reload(); } catch { } };
        menu.Items.Add(reload);

        menu.Items.Add(new Separator());

        var bookmark = new MenuItem { Header = _bookmarkedUrls.Contains(tab.CurrentUrl ?? "") ? "Remove Bookmark" : "Add Bookmark" };
        bookmark.Click += (_, _) => { _currentTab = tab; ToggleBookmark(); };
        menu.Items.Add(bookmark);

        menu.Items.Add(new Separator());

        var inspect = new MenuItem { Header = "Inspect Element" };
        inspect.Click += (_, _) => OpenDevTools();
        menu.Items.Add(inspect);

        menu.PlacementTarget = tab.WebView;
        menu.IsOpen = true;
    }

    // -----------------------------------------------------
    //  DevTools
    // -----------------------------------------------------

    private void OpenDevTools()
    {
        try { _currentTab?.WebView?.CoreWebView2?.OpenDevToolsWindow(); }
        catch { }
    }

    // -----------------------------------------------------
    //  Menu
    // -----------------------------------------------------

    private void ShowMenu()
    {
        var menu = new ContextMenu();
        menu.Background = new SolidColorBrush(Color.FromRgb(20, 20, 40));
        menu.BorderBrush = new SolidColorBrush(Color.FromRgb(46, 46, 82));
        menu.Foreground = new SolidColorBrush(Color.FromRgb(241, 241, 246));

        var newProfile = new MenuItem { Header = "New Profile..." };
        newProfile.Click += (_, _) => AddProfile();
        menu.Items.Add(newProfile);

        var delProfile = new MenuItem { Header = $"Delete '{_profiles.Current.Name}' Profile" };
        delProfile.Click += (_, _) => DeleteCurrentProfile();
        menu.Items.Add(delProfile);

        menu.Items.Add(new Separator());

        var bookmarks = new MenuItem { Header = "Bookmarks Manager" };
        bookmarks.Click += (_, _) => ShowBookmarksManager();
        menu.Items.Add(bookmarks);

        var history = new MenuItem { Header = "History" };
        history.Click += (_, _) => ShowHistoryManager();
        menu.Items.Add(history);

        var settings = new MenuItem { Header = "Settings" };
        settings.Click += (_, _) => ShowSettings();
        menu.Items.Add(settings);

        menu.Items.Add(new Separator());

        var zoomIn = new MenuItem { Header = "Zoom In  (Ctrl++)" };
        zoomIn.Click += (_, _) => ZoomIn();
        menu.Items.Add(zoomIn);

        var zoomOut = new MenuItem { Header = "Zoom Out (Ctrl+-)" };
        zoomOut.Click += (_, _) => ZoomOut();
        menu.Items.Add(zoomOut);

        var zoomReset = new MenuItem { Header = "Reset Zoom (Ctrl+0)" };
        zoomReset.Click += (_, _) => ZoomReset();
        menu.Items.Add(zoomReset);

        menu.Items.Add(new Separator());

        var fullscreen = new MenuItem { Header = _isFullscreen ? "Exit Fullscreen (F11)" : "Fullscreen (F11)" };
        fullscreen.Click += (_, _) => ToggleFullscreen();
        menu.Items.Add(fullscreen);

        var devtools = new MenuItem { Header = "Developer Tools (Ctrl+Shift+I)" };
        devtools.Click += (_, _) => OpenDevTools();
        menu.Items.Add(devtools);

        menu.Items.Add(new Separator());

        var about = new MenuItem { Header = $"About Flux Browser v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(2) ?? "1.0"}" };
        about.Click += (_, _) => ShowAbout();
        menu.Items.Add(about);

        menu.PlacementTarget = MenuBtn;
        menu.IsOpen = true;
    }

    private void ShowAbout()
    {
        if (_currentTab?.WebView == null) return;
        _currentTab.WebView.NavigateToString(
            "<html><body style='background:#0d0d1a; color:#f1f1f6; font-family:system-ui; " +
            "display:flex; align-items:center; justify-content:center; height:100vh; margin:0;'>" +
            "<div style='text-align:center;'>" +
            "<h1 style='color:#6366f1;'>\uD83C\uDF10 Flux Browser</h1>" +
            "<p style='color:#9898b8;'>Built with WPF + WebView2</p>" +
            "<p style='color:#585878;'>Default search: DuckDuckGo</p>" +
            "<p style='color:#585878;'>Profile: " + _profiles.Current.Name + "</p></div></body></html>");
    }

    private void ShowSettings()
    {
        if (_settingsManager == null) return;
        var dialog = new SettingsWindow(_settingsManager);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
        {
            _settingsManager.Save();
            StatusText.Text = "Settings saved";
        }
    }

    // -----------------------------------------------------
    //  Utility
    // -----------------------------------------------------

    private static string GetUniqueFilePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 1; i < 100; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        return path;
    }

    // -----------------------------------------------------
    //  Event Handlers
    // -----------------------------------------------------

    private void AddTab_Click(object sender, RoutedEventArgs e) => AddNewTab(CurrentHomePage, true);
    private void BackBtn_Click(object sender, RoutedEventArgs e) { try { _currentTab?.WebView?.GoBack(); } catch { } }
    private void ForwardBtn_Click(object sender, RoutedEventArgs e) { try { _currentTab?.WebView?.GoForward(); } catch { } }

    private void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTab?.WebView == null) return;
        try
        {
            if (_currentTab.IsLoading) try { _currentTab.WebView.CoreWebView2?.Stop(); } catch { }
            else _currentTab.WebView.Reload();
        }
        catch { }
    }

    private void HomeBtn_Click(object sender, RoutedEventArgs e) => NavigateCurrent(CurrentHomePage);
    private void BookmarkBtn_Click(object sender, RoutedEventArgs e) => ToggleBookmark();
    private void MenuBtn_Click(object sender, RoutedEventArgs e) => ShowMenu();

    private void UrlBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            NavigateCurrent(NormalizeUrl(UrlBox.Text));
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void UrlBox_GotFocus(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(UrlBox.SelectAll);

    private void UrlBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_currentTab != null) UpdateUrlBar(_currentTab.CurrentUrl ?? "");
    }

    // -----------------------------------------------------
    //  Window Controls
    // -----------------------------------------------------

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { ToggleMaximize(); return; }
        var source = e.OriginalSource as DependencyObject;
        while (source != null)
        {
            if (source is ComboBox || source is TextBox || source is Button || source is ListBoxItem) return;
            source = VisualTreeHelper.GetParent(source);
        }
        DragMove();
    }

    private void MinimizeBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeBtn_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        WindowState = _isMaximized ? WindowState.Normal : WindowState.Maximized;
        _isMaximized = !_isMaximized;
    }

    // -----------------------------------------------------
    //  Keyboard Shortcuts
    // -----------------------------------------------------

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
        bool shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

        if (ctrl && shift && e.Key == Key.I)
        { OpenDevTools(); e.Handled = true; }

        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.T: AddNewTab(CurrentHomePage, true); e.Handled = true; break;
                case Key.W: if (_currentTab != null) CloseTab(_currentTab); e.Handled = true; break;
                case Key.L: case Key.D: UrlBox.Focus(); e.Handled = true; break;
                case Key.R: RefreshBtn_Click(this, new RoutedEventArgs()); e.Handled = true; break;
                case Key.F: FindInPage(); e.Handled = true; break;
                case Key.N: AddNewTab(CurrentHomePage, true); e.Handled = true; break;
                case Key.OemPlus: case Key.Add: ZoomIn(); e.Handled = true; break;
                case Key.OemMinus: case Key.Subtract: ZoomOut(); e.Handled = true; break;
                case Key.D0: case Key.NumPad0: ZoomReset(); e.Handled = true; break;
                case Key.Tab: SwitchTab(shift ? -1 : 1); e.Handled = true; break;
            }
        }

        if (e.Key == Key.F5) { RefreshBtn_Click(this, new RoutedEventArgs()); e.Handled = true; }
        if (e.Key == Key.F6) { UrlBox.Focus(); e.Handled = true; }
        if (e.Key == Key.F11) { ToggleFullscreen(); e.Handled = true; }
        if (e.Key == Key.Escape)
        {
            if (FindBar.Visibility == Visibility.Visible) { FindClose(); e.Handled = true; }
            else if (_isFullscreen) { ToggleFullscreen(); e.Handled = true; }
            else { try { _currentTab?.WebView?.CoreWebView2?.Stop(); } catch { } e.Handled = true; }
        }

        base.OnKeyDown(e);
    }

    private void SwitchTab(int direction)
    {
        if (_tabs.Count < 2 || _currentTab == null) return;
        SelectTab(_tabs[(_tabs.IndexOf(_currentTab) + direction + _tabs.Count) % _tabs.Count]);
    }
}
