// VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using VoiceDucker.Audio;
using VoiceDucker.Model;

namespace VoiceDucker;

/// <summary>Displays application state and forwards commands to the application model.</summary>
public sealed partial class MainWindow : Window
{
    #region Private fields

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
    private string? _lastRenderError;
    private readonly Storyboard _mixerPulse = new();
    private readonly Dictionary<string, StreamRow> _streamRows = new(StringComparer.Ordinal);
    private readonly WindowPlacementService _windowPlacement;
    private readonly bool _restoredWindowPlacement;

    #endregion

    #region Initialization

    /// <summary>
    /// Creates the model and view adapters, restores layout, and subscribes the view to snapshots.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        ConfigureNumericControls();

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        AddOpacityPulse(_mixerPulse, MixerOnImage, minimumOpacity: 0);

        _model = new VoiceDuckerModel(this);
        ConfigureListeningControls();
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

    #endregion

    #region Public methods and events

    /// <summary>Forwards the application's emergency shutdown command without reading audio state.</summary>
    internal bool StopAudioForMemoryRecovery() => _model.StopAudioForMemoryRecovery();

    /// <summary>Reveals the existing window without replacing its controls or scroll position.</summary>
    internal void ShowForRepeatedLaunch()
    {
        if (_tray is null)
        {
            Activate();
        }
        else
        {
            _tray.ShowWindow();
        }

        ExistingInstanceNotice.IsOpen = true;
    }

    #endregion

    #region Implementation

    /// <summary>Finishes view setup after layout exists, then asks the model to initialize.</summary>
    private async void OnRootLoaded(object sender, RoutedEventArgs args)
    {
        RootPanel.Loaded -= OnRootLoaded;

        if (!_restoredWindowPlacement)
        {
            FitMainWindow(preservePosition: false);
        }

        UpdateSettingsPaneWidth();
        _windowPlacement.StartTracking();

        try
        {
            var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            _tray = new TrayIconService(handle, iconPath, () =>
            {
                var state = _model.Current;
                return (state.TrayMonitoringText, state.CanToggle);
            });
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

        try
        {
            await _model.InitializeAsync();
        }
        catch (Exception exception)
        {
            _model.ReportViewError($"Application initialization failed: {exception.Message}");
        }

        if (!_viewClosed)
        {
            RenderState(_model.Current);
        }
    }

    /// <summary>Coalesces notifications into one UI render of the latest available snapshot.</summary>
    private void OnModelStateChanged(ApplicationState state)
    {
        if (_viewClosed || Interlocked.Exchange(ref _renderQueued, 1) != 0)
        {
            return;
        }

        if (!DispatcherQueue.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _renderQueued, 0);

            if (!_viewClosed)
            {
                RenderState(_model.Current);
            }
        }))
        {
            Interlocked.Exchange(ref _renderQueued, 0);
        }
    }

    /// <summary>Projects model state onto controls while suppressing commands caused by rendering.</summary>
    private void RenderState(ApplicationState state)
    {
        if (_viewClosed)
        {
            return;
        }

        _rendering = true;

        try
        {
            RenderSettings(state);
            RenderListeningControls(state);
            FeedbackText.Text = state.Feedback;
            StatusText.Text = state.StatusText;
            VisualStateText.Text = state.VisualStateText;
            ToggleButtonLabel.Text = state.MonitoringButtonText;
            ToggleButtonIcon.Glyph = state.Audio.Running || state.IsPaused ? "\uE769" : "\uE768";
            ToggleButton.IsEnabled = state.CanToggle;
            MicrophoneComboBox.IsEnabled = state.CanSelectMicrophone;
            MicrophoneNameText.Text = state.MicrophoneNameText;
            MicrophoneLevelText.Text = state.MicrophoneLevelText;
            MicrophoneLevelHint.Text = state.MicrophoneLevelHint;

            MicrophoneLevelColumn.Width = new GridLength(state.MicrophoneMeterFraction, GridUnitType.Star);
            MicrophoneEmptyColumn.Width = new GridLength(1 - state.MicrophoneMeterFraction, GridUnitType.Star);
            SetMicLedActive(state.Audio.Running);
            MixerImage.Opacity = state.Audio.Running ? 1 : 0.6;

            var animationLevel = state.AnimationLevel;
            var mixerScaleFactor = 1 + animationLevel * 0.035;
            MixerScale.ScaleX = mixerScaleFactor;
            MixerScale.ScaleY = mixerScaleFactor;
            MicGlow.Opacity = state.Audio.Speaking ? 0.28 + animationLevel * 0.55 : 0;

            var bars = new[] { Bar1, Bar2, Bar3, Bar4, Bar5 };
            var weights = new[] { 0.45, 0.75, 1.0, 0.7, 0.4 };

            for (var index = 0; index < bars.Length; index++)
            {
                bars[index].Height = 6 + animationLevel * 26 * weights[index];
                bars[index].Opacity = state.Audio.Running ? 0.5 + animationLevel * 0.5 : 0.25;
            }

            var license = state.License;

            if (Title != license.WindowTitle)
            {
                Title = license.WindowTitle;
            }

            FooterPurchasePanel.Visibility = license.FooterVisible ? Visibility.Visible : Visibility.Collapsed;
            FooterTrialText.Text = license.FooterText;
            LicensePanel.Visibility = license.PanelVisible ? Visibility.Visible : Visibility.Collapsed;
            LicenseStatusText.Text = license.StatusText;
            _tray?.SetStatus(state.VisualStateText);

            // Keep readiness and microphone status current even if an optional row fails.
            RenderPlaybackRows(state.PlaybackRows);
            RefreshHiddenApps(state.HiddenProcesses);
            _lastRenderError = null;
        }
        catch (Exception exception)
        {
            var error = exception.ToString();
            FeedbackText.Text = $"Window could not be updated: {exception.Message}";

            // Reporting publishes another snapshot. The same failure must not feed itself
            // indefinitely, allocating controls and starving normal dispatcher work.
            if (_lastRenderError != error)
            {
                _lastRenderError = error;
                AudioDiagnostics.Write(() => $"view-render-error error={error}");
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_viewClosed)
                    {
                        _model.ReportViewError($"Window could not be updated: {exception.Message}");
                    }
                });
            }
        }
        finally
        {
            _rendering = false;
        }

        if (_viewReady)
        {
            PresentPendingNotices(state);
        }
    }

    /// <summary>Displays notices already requested by the model once the view can present them.</summary>
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
            catch (Exception exception)
            {
                _model.ReportViewError($"Monitoring reminder could not be shown: {exception.Message}");
            }
        }

        if (state.TrialExpiredNoticePending && !_trialDialogOpen)
        {
            _ = ShowTrialExpiredNoticeAsync();
        }
    }

    /// <summary>Shows the model's pending trial notice and forwards the user's purchase choice.</summary>
    private async Task ShowTrialExpiredNoticeAsync()
    {
        _trialDialogOpen = true;
        _model.DismissTrialExpiredNotice();

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

            if (result == ContentDialogResult.Primary)
            {
                await _model.PurchaseAsync();
            }
            else if (result == ContentDialogResult.Secondary)
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/umbertotechnopreneur/VoiceDucker"));
            }
        }
        catch (Exception)
        {
            if (!_viewClosed)
            {
                _model.ReportViewError("Your trial has ended. Use Purchase to open Microsoft Store, or visit VoiceDucker on GitHub.");
            }
        }
        finally
        {
            _trialDialogOpen = false;
        }
    }

    /// <summary>Keeps one settings section expanded and sends its persisted name to the model.</summary>
    private void SettingsExpander_Expanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        if (_openSettingsExpander != sender)
        {
            if (_openSettingsExpander is not null)
            {
                _openSettingsExpander.IsExpanded = false;
            }

            _openSettingsExpander = sender;
        }

        if (!_rendering)
        {
            _model.SetSettingsSection(sender == DetectionExpander ? "microphone"
                : sender == VolumeExpander ? "volume" : sender == ProfilesExpander ? "profiles" : "apps");
        }
    }

    /// <summary>Opens or closes the settings pane and saves the resulting window layout.</summary>
    private void SettingsButton_Click(object sender, RoutedEventArgs args)
    {
        SettingsSplitView.IsPaneOpen = SettingsButton.IsChecked == true;
        FitMainWindow();
        _windowPlacement.SaveNow();
    }

    /// <summary>Fits a restored window to its content without resizing a maximized window.</summary>
    private void FitMainWindow(bool preservePosition = true)
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored })
        {
            WindowContentSizing.Fit(this, RootPanel, 520, preservePosition: preservePosition,
                additionalWidth: SettingsSplitView.IsPaneOpen ? 520 : 0);
        }

        UpdateSettingsPaneWidth();
    }

    /// <summary>Converts physical client width into an equal-width settings pane in XAML units.</summary>
    private void UpdateSettingsPaneWidth()
    {
        var scale = Math.Max(0.1, RootPanel.XamlRoot?.RasterizationScale ?? 1);
        SettingsSplitView.OpenPaneLength = AppWindow.ClientSize.Width / scale / 2;
    }

    /// <summary>Forwards the monitoring toggle without implementing capture policy in the view.</summary>
    private async void ToggleButton_Click(object sender, RoutedEventArgs args) =>
        await _model.ToggleMonitoringAsync();

    /// <summary>Asks the model to refresh the Store license.</summary>
    private async void CheckLicense_Click(object sender, RoutedEventArgs args) =>
        await _model.RefreshLicenseAsync();

    /// <summary>Forwards the user's explicit purchase request.</summary>
    private async void Purchase_Click(object sender, RoutedEventArgs args) =>
        await _model.PurchaseAsync();

    /// <summary>Adds a repeating opacity animation to a presentation-only indicator.</summary>
    private static void AddOpacityPulse(Storyboard storyboard, DependencyObject target, double minimumOpacity)
    {
        var animation = new DoubleAnimation
        {
            From = minimumOpacity,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(900)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, "Opacity");
        storyboard.Children.Add(animation);
    }

    /// <summary>Pulses the computer artwork only when its displayed microphone state changes.</summary>
    private void SetMicLedActive(bool active)
    {
        if (_micLedActive == active)
        {
            return;
        }

        _micLedActive = active;
        if (active)
        {
            _mixerPulse.Begin();
        }
        else
        {
            _mixerPulse.Stop();
            MixerOnImage.Opacity = 0;
        }
    }

    /// <summary>Opens the About window from the main footer.</summary>
    private void AboutButton_Click(object sender, RoutedEventArgs args) => ShowAbout();

    /// <summary>Activates the existing About window or creates one when none is open.</summary>
    private void ShowAbout()
    {
        if (_aboutWindow is not null)
        {
            _aboutWindow.Activate();
            return;
        }

        _aboutWindow = new AboutWindow(App.ReadDisplayVersion());
        _aboutWindow.Closed += (_, _) => _aboutWindow = null;
        _aboutWindow.Activate();
    }

    /// <summary>Intercepts the native close request until the user chooses tray mode or exit.</summary>
    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        _windowPlacement.SaveNow();

        if (_allowClose)
        {
            return;
        }

        args.Cancel = true;

        if (_closeChoice is not null)
        {
            _closeChoice.Activate();
            return;
        }

        _closeChoice = new CloseChoiceWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        _closeChoice.MinimizeRequested += MinimizeToTray;
        _closeChoice.ExitRequested += ExitApplication;
        _closeChoice.Closed += (_, _) => _closeChoice = null;
        _closeChoice.Activate();
    }

    /// <summary>Hides the window through the tray adapter while leaving model state unchanged.</summary>
    private void MinimizeToTray()
    {
        try
        {
            if (_tray is null)
            {
                throw new InvalidOperationException("The tray icon is unavailable.");
            }

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

    /// <summary>Saves layout and permits the close request that begins application shutdown.</summary>
    private void ExitApplication()
    {
        _windowPlacement.SaveNow();
        _allowClose = true;
        Close();
    }

    /// <summary>Unsubscribes the closed view and releases every adapter even if earlier cleanup fails.</summary>
    private void OnClosed(object sender, WindowEventArgs args)
    {
        _viewClosed = true;
        _model.StateChanged -= OnModelStateChanged;
        _windowPlacement.SizeChanged -= UpdateSettingsPaneWidth;
        _mixerPulse.Stop();
        AppWindow.Closing -= OnWindowClosing;
        try
        {
            _windowPlacement.Dispose();
        }
        finally
        {
            try
            {
                _tray?.Dispose();
            }
            finally
            {
                try
                {
                    _aboutWindow?.Close();
                }
                finally
                {
                    try
                    {
                        _closeChoice?.Close();
                    }
                    finally
                    {
                        _model.Dispose();
                    }
                }
            }
        }
    }

    #endregion
}
