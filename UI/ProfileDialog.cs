using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FluxBrowser;

public class ProfileDialog : Window
{
    public string ProfileName { get; private set; } = "";
    public string ProfileIcon { get; private set; } = "\uD83D\uDC64";
    public string ProfileColor { get; private set; } = "#7c5fff";

    private readonly TextBox _nameBox;
    private readonly ListBox _iconList;

    private static readonly string[] Icons =
        ["\uD83D\uDC64", "\uD83D\uDC65", "\uD83D\uDC66", "\uD83D\uDC67", "\uD83D\uDC68", "\uD83D\uDC69",
         "\uD83D\uDC71", "\uD83D\uDC76", "\uD83D\uDC77", "\uD83C\uDF93", "\uD83C\uDFEB", "\uD83D\uDCBC",
         "\uD83D\uDCBB", "\uD83C\uDFA8", "\uD83D\uDD0D", "\uD83C\uDF0D", "\uD83D\uDE80", "\uD83C\uDF1F"];

    private static readonly string[] Colors =
        ["#7c5fff", "#e94560", "#51cf66", "#ffd43b", "#339af0", "#f06595",
         "#845ef7", "#20c997", "#ff922b", "#748ffc", "#38d9a9", "#f783ac"];

    public ProfileDialog()
    {
        Title = "New Profile"; Width = 340; Height = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.ToolWindow; ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(20, 20, 40));
        Foreground = new SolidColorBrush(Color.FromRgb(241, 241, 246));

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        grid.Children.Add(new TextBlock { Text = "Profile Name:", Margin = new Thickness(0, 0, 0, 4), FontSize = 13 });

        _nameBox = new TextBox
        {
            Text = "New Profile", Margin = new Thickness(0, 0, 0, 12), FontSize = 13,
            Background = new SolidColorBrush(Color.FromRgb(28, 28, 54)),
            Foreground = new SolidColorBrush(Color.FromRgb(241, 241, 246)),
            BorderThickness = new Thickness(0), Padding = new Thickness(8, 4, 8, 4),
            CaretBrush = new SolidColorBrush(Color.FromRgb(99, 102, 241))
        };
        Grid.SetRow(_nameBox, 1); grid.Children.Add(_nameBox);

        var iconLabel = new TextBlock { Text = "Icon:", Margin = new Thickness(0, 0, 0, 4), FontSize = 13 };
        Grid.SetRow(iconLabel, 2); grid.Children.Add(iconLabel);

        _iconList = new ListBox
        {
            ItemsSource = Icons, Margin = new Thickness(0, 0, 0, 12), Height = 38,
            Background = new SolidColorBrush(Color.FromRgb(28, 28, 54)),
            BorderThickness = new Thickness(0), SelectedIndex = 0
        };
        Grid.SetRow(_iconList, 3); grid.Children.Add(_iconList);

        var layout = new StackPanel();
        layout.Children.Add(grid);

        layout.Children.Add(new TextBlock { Text = "Color:", Margin = new Thickness(16, 0, 0, 4), FontSize = 13 });

        var colorList = new ListBox
        {
            ItemsSource = Colors, Margin = new Thickness(16, 0, 16, 12), Height = 32,
            Background = new SolidColorBrush(Color.FromRgb(28, 28, 54)),
            BorderThickness = new Thickness(0), SelectedIndex = 0
        };
        colorList.SelectionChanged += (_, _) => ProfileColor = colorList.SelectedItem as string ?? ProfileColor;
        layout.Children.Add(colorList);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 0, 16, 16)
        };

        var okBtn = new Button
        {
            Content = "Create", Width = 80, Height = 30, Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand,
            Background = new SolidColorBrush(Color.FromRgb(99, 102, 241)),
            Foreground = Brushes.White, BorderThickness = new Thickness(0)
        };
        okBtn.Click += (_, _) =>
        {
            ProfileName = _nameBox.Text.Trim();
            if (!string.IsNullOrEmpty(ProfileName)) { DialogResult = true; Close(); }
        };
        btnPanel.Children.Add(okBtn);

        var cancelBtn = new Button
        {
            Content = "Cancel", Width = 80, Height = 30, Cursor = Cursors.Hand,
            Background = new SolidColorBrush(Color.FromRgb(60, 60, 80)),
            Foreground = new SolidColorBrush(Color.FromRgb(241, 241, 246)),
            BorderThickness = new Thickness(0)
        };
        cancelBtn.Click += (_, _) => { DialogResult = false; Close(); };
        btnPanel.Children.Add(cancelBtn);

        Content = new StackPanel { Children = { layout, btnPanel } };
        Loaded += (_, _) => _nameBox.Focus();
    }
}
