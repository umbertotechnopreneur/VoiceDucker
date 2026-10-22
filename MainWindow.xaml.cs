// VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using VoiceDucker.Model;
using Windows.Foundation;
using Windows.UI;

namespace VoiceDucker;

/// <summary>Displays application state and forwards commands to the application model.</summary>
public sealed partial class MainWindow : Window
{
    private readonly VoiceDuckerModel _model;
    private TrayIconService? _tray;
    private AboutWindow? _aboutWindow;
    private CloseChoiceWindow? _closeChoice;
    private Expander? _openSettingsExpander;
    private bool _allowClose;
    private bool _micLedActive;
    private bool _rendering = true;
    private bool _viewReady;
    private bool _viewClosed;
    private bool _trialDialogOpen;
    private int _renderQueued;
    private readonly Storyboard _micLedPulse = new();
    private readonly Storyboard _mixerPulse = new();
    private readonly Dictionary<string, StreamRow> _streamRows = new(StringComparer.Ordinal);
    private readonly WindowPlacementService _windowPlacement;
    private readonly bool _restoredWindowPlacement;
    private readonly DispatcherTimer _footerTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly Storyboard _footerFade = new();
    private bool _footerPointerOver;

    public MainWindow()
    {
        InitializeComponent();
        var footerFade = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = new Duration(TimeSpan.FromSeconds(3))
        };
        Storyboard.SetTarget(footerFade, FooterLinks);
        Storyboard.SetTargetProperty(footerFade, "Opacity");
        _footerFade.Children.Add(footerFade);
        _footerFade.Completed += (_, _) => FooterLinks.IsHitTestVisible = false;
        _footerTimer.Tick += FooterTimer_Tick;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        AddOpacityPulse(_micLedPulse, MicStatusIcon, minimumOpacity: 0.15);
        AddOpacityPulse(_mixerPulse, MixerOnImage, minimumOpacity: 0);

        _model = new VoiceDuckerModel(this);
        _windowPlacement = new WindowPlacementService(this, () => SettingsSplitView.IsPaneOpen,
            _model.ReportViewError, _model.LoadWindowPlacement, _model.SaveWindowPlacement);
        _restoredWindowPlacement = _windowPlacement.TryRestore(out var settingsPaneOpen);
        SettingsSplitView.IsPaneOpen = settingsPaneOpen;
        SettingsButton.IsChecked = settingsPaneOpen;
        _windowPlacement.SizeChanged += UpdateSettingsPaneWidth;
        _model.StateChanged += OnModelStateChanged;
        AppWindow.Closing += OnWindowClosing;
        RootPanel.Loaded += OnRootLoaded;
        Closed += OnClosed;
        RenderState(_model.Current);
    }

    internal void ShowForRepeatedLaunch()
    {
        if (_tray is null) Activate();
        else _tray.ShowWindow();
        ExistingInstanceNotice.IsOpen = true;
    }

    private async void OnRootLoaded(object sender, RoutedEventArgs args)
    {
        RootPanel.Loaded -= OnRootLoaded;
        if (!_restoredWindowPlacement) FitMainWindow(preservePosition: false);
        UpdateSettingsPaneWidth();
        RevealFooter();
        _windowPlacement.StartTracking();
        try
        {
            var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            _tray = new TrayIconService(handle, iconPath, () => _model.Current.Audio.Running);
            _tray.ToggleRequested += () => _ = _model.ToggleMonitoringAsync();
            _tray.AboutRequested += ShowAbout;
            _tray.ExitRequested += ExitApplication;
            _tray.Error += exception => _model.ReportViewError($"Tray error: {exception.Message}");
        }
        catch (Exception exception)
        {
            _model.ReportViewError($"Tray is unavailable: {exception.Message}");
        }
        _viewReady = true;
        try { await _model.InitializeAsync(); }
        catch (Exception exception) { _model.ReportViewError($"Application initialization failed: {exception.Message}"); }
        if (!_viewClosed) RenderState(_model.Current);
    }

    private void OnModelStateChanged(ApplicationState state)
    {
        if (_viewClosed || Interlocked.Exchange(ref _renderQueued, 1) != 0) return;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _renderQueued, 0);
            if (!_viewClosed) RenderState(_model.Current);
        }))
            Interlocked.Exchange(ref _renderQueued, 0);
    }

    private void RenderState(ApplicationState state)
    {
        if (_viewClosed) return;
        _rendering = true;
        try
        {
            RenderSettings(state);
            RenderPlaybackRows(state.PlaybackRows);
            RefreshHiddenApps(state.HiddenProcesses);
            FeedbackText.Text = state.Feedback;
            StatusText.Text = state.StatusText;
            VisualStateText.Text = state.VisualStateText;
            ToggleButtonLabel.Text = state.Audio.Running ? "Disable" : "Enable";
            ToggleButtonIcon.Glyph = state.Audio.Running ? "\uE769" : "\uE768";
            ToggleButton.IsEnabled = state.CanToggle;
            MicrophoneComboBox.IsEnabled = state.CanSelectMicrophone;
            MicrophoneNameText.Text = state.MicrophoneNameText;
            MicrophoneLevelText.Text = state.MicrophoneLevelText;
            MicrophoneLevelHint.Text = state.MicrophoneLevelHint;
            MicrophoneLevelColumn.Width = new GridLength(state.MicrophoneMeterFraction, GridUnitType.Star);
            MicrophoneEmptyColumn.Width = new GridLength(1 - state.MicrophoneMeterFraction, GridUnitType.Star);
            SetMicLedActive(state.Audio.Running);
            MixerImage.Opacity = state.Audio.Running ? 1 : 0.6;
            var level = state.AnimationLevel;
            var scale = 1 + level * 0.035;
            MixerScale.ScaleX = scale;
            MixerScale.ScaleY = scale;
            MicGlow.Opacity = state.Audio.Speaking ? 0.28 + level * 0.55 : 0;
            var bars = new[] { Bar1, Bar2, Bar3, Bar4, Bar5 };
            var weights = new[] { 0.45, 0.75, 1.0, 0.7, 0.4 };
            for (var index = 0; index < bars.Length; index++)
            {
                bars[index].Height = 6 + level * 26 * weights[index];
                bars[index].Opacity = state.Audio.Running ? 0.5 + level * 0.5 : 0.25;
            }

            var license = state.License;
            if (Title != license.WindowTitle) Title = license.WindowTitle;
            FooterPurchasePanel.Visibility = license.FooterVisible ? Visibility.Visible : Visibility.Collapsed;
            FooterTrialText.Text = license.FooterText;
            LicensePanel.Visibility = license.PanelVisible ? Visibility.Visible : Visibility.Collapsed;
            LicenseStatusText.Text = license.StatusText;
            _tray?.SetStatus(state.VisualStateText);
        }
        catch (Exception exception)
        {
            // Do not synchronously publish another render while reporting a view failure.
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_viewClosed) _model.ReportViewError($"Window could not be updated: {exception.Message}");
            });
        }
        finally { _rendering = false; }

        if (_viewReady) PresentPendingNotices(state);
    }

    private void PresentPendingNotices(ApplicationState state)
    {
        if (state.MonitoringReminderPending && _tray is not null)
        {
            _model.DismissMonitoringReminder();
            try
            {
                _tray.ShowNotification("Monitoring is off",
                    "VoiceDucker is not monitoring your microphone. Press Enable when you're ready.");
            }
            catch (Exception exception) { _model.ReportViewError($"Monitoring reminder could not be shown: {exception.Message}"); }
        }
        if (state.TrialExpiredNoticePending && !_trialDialogOpen)
            _ = ShowTrialExpiredNoticeAsync();
    }

    private async Task ShowTrialExpiredNoticeAsync()
    {
        _trialDialogOpen = true;
        _model.DismissTrialExpiredNotice();
        RevealFooter();
        try
        {
            AppWindow.Show();
            Activate();
            var dialog = new ContentDialog
            {
                XamlRoot = RootPanel.XamlRoot,
                Title = "Your free trial has ended",
                Content = "Thank you for trying VoiceDucker. Your 30-day free trial has ended. Visit Microsoft Store to purchase your copy of the app. You can also visit GitHub to study, copy and adapt the source under its MIT license.",
                PrimaryButtonText = "Open Microsoft Store",
                SecondaryButtonText = "View source on GitHub",
                CloseButtonText = "Close"
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary) await _model.PurchaseAsync();
            else if (result == ContentDialogResult.Secondary)
                await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/umbertotechnopreneur/VoiceDucker"));
        }
        catch (Exception)
        {
            if (!_viewClosed)
                _model.ReportViewError("Your trial has ended. Use Purchase to open Microsoft Store, or visit VoiceDucker on GitHub.");
        }
        finally { _trialDialogOpen = false; }
    }

    private void SettingsExpander_Expanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        if (_openSettingsExpander != sender)
        {
            if (_openSettingsExpander is not null) _openSettingsExpander.IsExpanded = false;
            _openSettingsExpander = sender;
        }
        if (!_rendering)
            _model.SetSettingsSection(sender == DetectionExpander ? "microphone"
                : sender == VolumeExpander ? "volume" : "apps");
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs args)
    {
        SettingsSplitView.IsPaneOpen = SettingsButton.IsChecked == true;
        FitMainWindow();
        _windowPlacement.SaveNow();
    }

    private void FitMainWindow(bool preservePosition = true)
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored })
            WindowContentSizing.Fit(this, RootPanel, 520, preservePosition: preservePosition,
                additionalWidth: SettingsSplitView.IsPaneOpen ? 520 : 0);
        UpdateSettingsPaneWidth();
    }

    private void UpdateSettingsPaneWidth()
    {
        var scale = Math.Max(0.1, RootPanel.XamlRoot?.RasterizationScale ?? 1);
        SettingsSplitView.OpenPaneLength = AppWindow.ClientSize.Width / scale / 2;
    }

    private async void ToggleButton_Click(object sender, RoutedEventArgs args) =>
        await _model.ToggleMonitoringAsync();

    private async void CheckLicense_Click(object sender, RoutedEventArgs args) =>
        await _model.RefreshLicenseAsync();

    private async void Purchase_Click(object sender, RoutedEventArgs args) =>
        await _model.PurchaseAsync();

    private static void AddOpacityPulse(Storyboard storyboard, DependencyObject target, double minimumOpacity)
    {
        var animation = new DoubleAnimation
        {
            From = minimumOpacity, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(900)),
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, "Opacity");
        storyboard.Children.Add(animation);
    }

    private void SetMicLedActive(bool active)
    {
        if (_micLedActive == active) return;
        _micLedActive = active;
        MicStatusBorder.Background = new SolidColorBrush(active
            ? Color.FromArgb(255, 6, 5, 7) : Color.FromArgb(255, 38, 59, 80));
        MicStatusBorder.BorderBrush = new SolidColorBrush(active
            ? Color.FromArgb(255, 116, 29, 39) : Color.FromArgb(255, 246, 162, 76));
        if (active)
        {
            var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            gradient.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 126, 24, 34), Offset = 0 });
            gradient.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 247, 112, 106), Offset = 0.39 });
            gradient.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 168, 38, 45), Offset = 0.71 });
            gradient.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 74, 14, 23), Offset = 1 });
            MicStatusIcon.Foreground = gradient;
            _micLedPulse.Begin();
            _mixerPulse.Begin();
        }
        else
        {
            MicStatusIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 246, 162, 76));
            _micLedPulse.Stop();
            _mixerPulse.Stop();
            MicStatusIcon.Opacity = 1;
            MixerOnImage.Opacity = 0;
        }
    }

    private void RevealFooter()
    {
        _footerFade.Stop();
        FooterLinks.Opacity = 1;
        FooterLinks.IsHitTestVisible = true;
        _footerTimer.Stop();
        _footerTimer.Start();
    }

    private void Footer_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        _footerPointerOver = true;
        RevealFooter();
    }

    private void Footer_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        _footerPointerOver = false;
        _footerTimer.Stop();
        _footerTimer.Start();
    }

    private void Footer_GotFocus(object sender, RoutedEventArgs args) => RevealFooter();

    private void FooterTimer_Tick(object? sender, object args)
    {
        var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(RootPanel.XamlRoot) as DependencyObject;
        while (focused is not null)
        {
            if (focused == FooterLinks) return;
            focused = VisualTreeHelper.GetParent(focused);
        }
        if (_footerPointerOver) return;
        _footerTimer.Stop();
        _footerFade.Begin();
    }

    private void AboutButton_Click(object sender, RoutedEventArgs args) => ShowAbout();

    private void ShowAbout()
    {
        if (_aboutWindow is not null) { _aboutWindow.Activate(); return; }
        _aboutWindow = new AboutWindow();
        _aboutWindow.Closed += (_, _) => _aboutWindow = null;
        _aboutWindow.Activate();
    }

    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        _windowPlacement.SaveNow();
        if (_allowClose) return;
        args.Cancel = true;
        if (_closeChoice is not null) { _closeChoice.Activate(); return; }
        _closeChoice = new CloseChoiceWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        _closeChoice.MinimizeRequested += MinimizeToTray;
        _closeChoice.ExitRequested += ExitApplication;
        _closeChoice.Closed += (_, _) => _closeChoice = null;
        _closeChoice.Activate();
    }

    private void MinimizeToTray()
    {
        try
        {
            if (_tray is null) throw new InvalidOperationException("The tray icon is unavailable.");
            _aboutWindow?.Close();
            _windowPlacement.SaveNow();
            _tray.HideWindow();
        }
        catch (Exception exception)
        {
            _model.ReportViewError($"Could not minimize to tray: {exception.Message}");
            _tray?.ShowWindow();
        }
    }

    private void ExitApplication()
    {
        _windowPlacement.SaveNow();
        _allowClose = true;
        Close();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _viewClosed = true;
        _model.StateChanged -= OnModelStateChanged;
        _windowPlacement.SizeChanged -= UpdateSettingsPaneWidth;
        _footerTimer.Stop();
        _footerTimer.Tick -= FooterTimer_Tick;
        _footerFade.Stop();
        _micLedPulse.Stop();
        _mixerPulse.Stop();
        AppWindow.Closing -= OnWindowClosing;
        try { _windowPlacement.Dispose(); }
        finally
        {
            try { _tray?.Dispose(); }
            finally
            {
                try { _aboutWindow?.Close(); }
                finally
                {
                    try { _closeChoice?.Close(); }
                    finally { _model.Dispose(); }
                }
            }
        }
    }
}
