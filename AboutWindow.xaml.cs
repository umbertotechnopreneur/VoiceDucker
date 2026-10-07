using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace VoiceDucker;

public sealed partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(390, 560));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        VersionText.Text = $"Version {typeof(App).Assembly.GetName().Version?.ToString(3) ?? "unknown"}";
    }

    private void Close_Click(object sender, RoutedEventArgs args) => Close();
}
