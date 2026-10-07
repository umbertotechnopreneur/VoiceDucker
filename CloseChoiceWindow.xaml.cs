using Microsoft.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Windowing;

namespace VoiceDucker;

public sealed partial class CloseChoiceWindow : Window
{
    private readonly AppWindow _owner;
    public event Action? MinimizeRequested;
    public event Action? ExitRequested;

    public CloseChoiceWindow(IntPtr ownerHandle)
    {
        InitializeComponent();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        NativeWindowOwner.SetOwner(WinRT.Interop.WindowNative.GetWindowHandle(this), ownerHandle);
        _owner = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(ownerHandle));
        ClosePanel.Loaded += OnClosePanelLoaded;
    }

    private void OnClosePanelLoaded(object sender, RoutedEventArgs args)
    {
        ClosePanel.Loaded -= OnClosePanelLoaded;
        WindowContentSizing.Fit(this, ClosePanel, 460, _owner);
    }

    private void Cancel_Click(object sender, RoutedEventArgs args) => Close();

    private void Minimize_Click(object sender, RoutedEventArgs args)
    {
        Close();
        MinimizeRequested?.Invoke();
    }

    private void CloseApp_Click(object sender, RoutedEventArgs args)
    {
        Close();
        ExitRequested?.Invoke();
    }
}
