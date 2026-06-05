using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluxBrowser.Models;
using FluxBrowser.Services;

namespace FluxBrowser;

public class SettingsWindow : Window
{
    private readonly SettingsManager _manager;
    private readonly ComboBox _searchCombo;
    private readonly TextBox _homePageBox;
    private readonly ComboBox _startupCombo;
    private readonly CheckBox _promptCheck;
    private readonly CheckBox _darkCheck;

    public SettingsWindow(SettingsManager manager)
    {
        _manager = manager;
        var s = manager.Settings;

        Title = "Settings - Flux Browser";
        Width = 460; Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.ToolWindow; ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(20, 20, 40));
        Foreground = new SolidColorBrush(Color.FromRgb(241, 241, 246));

        var root = new StackPanel { Margin = new Thickness(24) };

        root.Children.Add(new TextBlock
        {
            Text = "Settings", FontSize = 18, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(99, 102, 241)),
            Margin = new Thickness(0, 0, 0, 20)
        });

        root.Children.Add(MakeLabel("Default Search Engine"));
        _searchCombo = MakeCombo(BrowserSettings.SearchEngineNames, s.DefaultSearchEngine);
        root.Children.Add(_searchCombo);

        root.Children.Add(MakeLabel("Home Page"));
        _homePageBox = new TextBox
        {
            Text = s.HomePage, FontSize = 13, Height = 32,
            Background = new SolidColorBrush(Color.FromRgb(28, 28, 54)),
            Foreground = new SolidColorBrush(Color.FromRgb(241, 241, 246)),
            BorderThickness = new Thickness(0), Padding = new Thickness(10, 0, 10, 0),
            CaretBrush = new SolidColorBrush(Color.FromRgb(99, 102, 241)),
            Margin = new Thickness(0, 0, 0, 4)
        };
        root.Children.Add(_homePageBox);

        root.Children.Add(MakeLabel("On startup"));
        _startupCombo = MakeCombo(BrowserSettings.StartupBehaviorNames, s.StartupBehavior);
        root.Children.Add(_startupCombo);

        _promptCheck = new CheckBox
        {
            Content = " Ask before downloading files", FontSize = 13,
            IsChecked = s.PromptBeforeDownload,
            Foreground = new SolidColorBrush(Color.FromRgb(241, 241, 246)),
            Margin = new Thickness(0, 12, 0, 4)
        };
        root.Children.Add(_promptCheck);

        _darkCheck = new CheckBox
        {
            Content = " Dark mode", FontSize = 13,
            IsChecked = s.DarkMode,
            Foreground = new SolidColorBrush(Color.FromRgb(241, 241, 246)),
            Margin = new Thickness(0, 4, 0, 16)
        };
        root.Children.Add(_darkCheck);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var saveBtn = new Button
        {
            Content = "Save", Width = 80, Height = 32, Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand,
            Background = new SolidColorBrush(Color.FromRgb(99, 102, 241)),
            Foreground = Brushes.White, BorderThickness = new Thickness(0)
        };
        saveBtn.Click += Save_Click;
        btnPanel.Children.Add(saveBtn);

        var cancelBtn = new Button
        {
            Content = "Cancel", Width = 80, Height = 32, Cursor = Cursors.Hand,
            Background = new SolidColorBrush(Color.FromRgb(60, 60, 80)),
            Foreground = new SolidColorBrush(Color.FromRgb(241, 241, 246)),
            BorderThickness = new Thickness(0)
        };
        cancelBtn.Click += (_, _) => { DialogResult = false; Close(); };
        btnPanel.Children.Add(cancelBtn);

        root.Children.Add(btnPanel);
        Content = new ScrollViewer { Content = root };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var s = _manager.Settings;
        s.DefaultSearchEngine = ((KeyValuePair<string, string>)_searchCombo.SelectedItem).Key;
        s.HomePage = _homePageBox.Text.Trim();
        if (string.IsNullOrEmpty(s.HomePage)) s.HomePage = "https://duckduckgo.com";
        s.StartupBehavior = ((KeyValuePair<string, string>)_startupCombo.SelectedItem).Key;
        s.PromptBeforeDownload = _promptCheck.IsChecked == true;
        s.DarkMode = _darkCheck.IsChecked == true;
        _manager.Save();
        DialogResult = true;
        Close();
    }

    private static TextBlock MakeLabel(string text) => new()
    {
        Text = text, FontSize = 12,
        Foreground = new SolidColorBrush(Color.FromRgb(152, 152, 184)),
        Margin = new Thickness(0, 10, 0, 4)
    };

    private static ComboBox MakeCombo(Dictionary<string, string> items, string selectedKey)
    {
        var combo = new ComboBox
        {
            ItemsSource = items.ToList(),
            DisplayMemberPath = "Value",
            SelectedValuePath = "Key",
            SelectedValue = selectedKey,
            Height = 32, FontSize = 13,
            Background = new SolidColorBrush(Color.FromRgb(28, 28, 54)),
            Foreground = new SolidColorBrush(Color.FromRgb(241, 241, 246)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 0, 4)
        };
        return combo;
    }
}
