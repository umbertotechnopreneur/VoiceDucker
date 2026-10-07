using Microsoft.UI.Xaml;

namespace VoiceDucker;

public sealed partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        VersionText.Text = $"Version {typeof(App).Assembly.GetName().Version?.ToString(3) ?? "unknown"}";
        AboutPanel.Loaded += OnAboutPanelLoaded;
    }

    private void OnAboutPanelLoaded(object sender, RoutedEventArgs args)
    {
        AboutPanel.Loaded -= OnAboutPanelLoaded;
        WindowContentSizing.Fit(this, AboutPanel, 430);
    }

    private void Close_Click(object sender, RoutedEventArgs args) => Close();
}
