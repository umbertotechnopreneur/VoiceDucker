using Microsoft.UI.Xaml;
using VoiceDucker.Audio;
using Windows.Graphics;

namespace VoiceDucker;

public sealed partial class MainWindow : Window
{
    private readonly DuckingEngine _engine = new();

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(390, 330));
        AppWindow.SetIcon("Assets/AppIcon.ico");
        _engine.StatusChanged += OnStatusChanged;
        Closed += OnClosed;
    }

    private async void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleButton.IsEnabled = false;
        if (_engine.IsRunning)
        {
            await Task.Run(_engine.Stop);
        }
        else
        {
            _engine.Start();
        }
        ToggleButton.IsEnabled = true;
    }

    private void OnStatusChanged(EngineStatus status)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            StatusText.Text = status.Message;
            ToggleButton.Content = status.Running ? "Disable" : "Enable";
        });
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _engine.StatusChanged -= OnStatusChanged;
        _engine.Dispose();
    }
}
