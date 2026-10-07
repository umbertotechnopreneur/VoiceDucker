using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace VoiceDucker;

public sealed partial class CloseChoiceWindow : Window
{
    public event Action? MinimizeRequested;
    public event Action? ExitRequested;

    public CloseChoiceWindow(IntPtr ownerHandle)
    {
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(440, 235));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        NativeWindowOwner.SetOwner(WinRT.Interop.WindowNative.GetWindowHandle(this), ownerHandle);
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
