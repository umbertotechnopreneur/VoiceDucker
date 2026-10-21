/* VBWR B
 * Project: VoiceDucker
 * Repository: https://github.com/umbertotechnopreneur/VoiceDucker
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 * VibeWare initiative: Human intent, AI execution, and plenty of tokens
 * Created with AI: OpenAI Codex assisted the initial implementation; Git history
 * Modified with AI: OpenAI Codex; audio settings, reactive mixer, tray and About, 2026-10-08
 * Human guidance: Umberto Giacobbi defined the purpose and audio behavior
 * Evidence: Git history
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT
 * VBWR E */

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using VoiceDucker.Audio;
using VoiceDucker.Licensing;
using Windows.Foundation;
using Windows.UI;

namespace VoiceDucker;

/// <summary>
/// Coordinates the window, engine lifetime and desktop integration.
/// Settings and playback-row presentation live in the matching partial files.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly DuckingEngine _engine;
    private DuckingSettings _settings;
    private TrayIconService? _tray;
    private AboutWindow? _aboutWindow;
    private CloseChoiceWindow? _closeChoice;
    private bool _settingsReady;
    private readonly bool _isWindowsStartup;
    private Expander? _openSettingsExpander;
    private bool _allowClose;
    private bool _micLedActive;
    private readonly Storyboard _micLedPulse = new();
    private readonly Storyboard _mixerPulse = new();
    private readonly Dictionary<string, StreamRow> _streamRows = new(StringComparer.Ordinal);
    private PlaybackMonitor? _playbackMonitor;
    private DispatcherTimer? _streamTimer;
    private bool _refreshingStreamRows;
    private bool _refreshingMicrophones;
    private bool _microphoneOperationPending;
    private bool _microphoneMeterRunning;
    private bool _microphoneDetectionActive;
    private double _microphoneMeterRms;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly WindowPlacementService _windowPlacement;
    private readonly bool _restoredWindowPlacement;
    private readonly DispatcherTimer _footerTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly Storyboard _footerFade = new();
    private bool _footerPointerOver;
    private readonly StoreLicenseService _licensing;
    private readonly DispatcherTimer _licenseTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _trialExpiryShown;

    public MainWindow(bool isWindowsStartup = false)
    {
        _isWindowsStartup = isWindowsStartup;
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

        _settings = SettingsStore.Load(out var settingsError);
        _engine = new DuckingEngine(_settings);
        _licensing = new StoreLicenseService(this);
        _licensing.Changed += OnLicenseChanged;
        _licenseTimer.Tick += (_, _) => { _licensing.CheckExpiration(); UpdateLicenseTitle(); };
        ReductionBox.Value = _settings.ReductionPercent;
        FadeDownBox.Value = _settings.FadeDownMilliseconds;
        FadeUpBox.Value = _settings.FadeUpMilliseconds;
        RestoreDelayBox.Value = _settings.RestoreDelayMilliseconds;
        OtherSourcesCheckBox.IsChecked = _settings.IncludeOtherSources;
        StartupMicrophoneCheckBox.IsChecked = _settings.EnableMicrophoneOnWindowsStartup;
        SensitivitySlider.Value = _settings.MicrophoneGainDecibels;
        UpdateSensitivityLabel();
        ThresholdSlider.Value = _settings.ThresholdDecibels;
        HysteresisBox.Value = _settings.HysteresisDecibels;
        UpdateThresholdDisplay();
        RefreshHiddenApps();
        RestoreSettingsSection();
        _settingsReady = true;
        FeedbackText.Text = settingsError ?? string.Empty;
        RefreshMicrophoneChoices();

        _windowPlacement = new WindowPlacementService(this, () => SettingsSplitView.IsPaneOpen,
            message => FeedbackText.Text = message);
        _restoredWindowPlacement = _windowPlacement.TryRestore(out var settingsPaneOpen);
        SettingsSplitView.IsPaneOpen = settingsPaneOpen;
        SettingsButton.IsChecked = settingsPaneOpen;
        _windowPlacement.SizeChanged += UpdateSettingsPaneWidth;

        StartupCheckBox.IsEnabled = false;

        _engine.StatusChanged += OnStatusChanged;
        AppWindow.Closing += OnWindowClosing;
        RootPanel.Loaded += OnRootLoaded;
        Closed += OnClosed;
        _ = RemindWhenMonitoringIsOffAsync();
    }

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
        MainScrollViewer.ChangeView(null, 0, null);
    }

    private async void OnRootLoaded(object sender, RoutedEventArgs args)
    {
        RootPanel.Loaded -= OnRootLoaded;
        if (!_restoredWindowPlacement)
        {
            FitMainWindow(preservePosition: false);
        }

        UpdateSettingsPaneWidth();
        RevealFooter();
        if (_licensing.IsStoreEdition)
        {
            await UpdateLicenseUiAsync();
            _ = _licensing.RefreshAsync();
        }
        var playbackReady = false;
        try
        {
            _engine.CapturePlaybackSnapshot();
            _playbackMonitor = new PlaybackMonitor(_engine.VolumeSnapshot, _engine.VolumeEvents);
            _streamTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _streamTimer.Tick += OnStreamTimerTick;
            RefreshStreamRows();
            _streamTimer.Start();
            playbackReady = true;
        }
        catch (Exception exception)
        {
            FeedbackText.Text = $"Playback initialization failed: {exception.Message}";
        }
        _windowPlacement.StartTracking();
        try
        {
            var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            _tray = new TrayIconService(handle, iconPath, () => _engine.IsRunning);
            _tray.ToggleRequested += () => _ = ToggleDuckingAsync();
            _tray.AboutRequested += ShowAbout;
            _tray.ExitRequested += ExitApplication;
            _tray.Error += exception => FeedbackText.Text = $"Tray error: {exception.Message}";
        }
        catch (Exception exception)
        {
            FeedbackText.Text = $"Tray is unavailable: {exception.Message}";
        }
        try
        {
            await StartupRegistration.InitializeDefaultAsync();
            StartupCheckBox.IsChecked = await StartupRegistration.IsEnabledAsync();
        }
        catch (Exception exception)
        {
            FeedbackText.Text = $"Windows startup status could not be read: {exception.Message}";
        }
        finally
        {
            StartupCheckBox.IsEnabled = true;
            StartupMicrophoneCheckBox.IsEnabled = StartupCheckBox.IsChecked == true;
        }

        if (playbackReady && _settings.ShouldEnableMicrophoneOnLaunch(_isWindowsStartup) &&
            !_engine.IsRunning && !_lifetime.IsCancellationRequested)
        {
            await ToggleDuckingAsync();
        }
    }

    private void SettingsExpander_Expanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        if (_openSettingsExpander != sender)
        {
            if (_openSettingsExpander is not null) _openSettingsExpander.IsExpanded = false;
            _openSettingsExpander = sender;
        }
        if (_settingsReady)
        {
            _settings = _settings with
            {
                LastSettingsSection = sender == DetectionExpander ? "microphone"
                    : sender == VolumeExpander ? "volume" : "apps"
            };
            ApplyAndSaveSettings();
        }
    }

    private void RestoreSettingsSection()
    {
        var section = _settings.LastSettingsSection switch
        {
            "microphone" => DetectionExpander,
            "volume" => VolumeExpander,
            "apps" => AppsExpander,
            _ => null
        };
        if (section is not null) section.IsExpanded = true;
    }

    private async Task RemindWhenMonitoringIsOffAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), _lifetime.Token);
            if (!_engine.IsRunning)
            {
                _tray?.ShowNotification("Monitoring is off",
                    "VoiceDucker is not monitoring your microphone. Press Enable when you're ready.");
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The application closed before the one-time startup reminder was due.
        }
        catch (Exception exception)
        {
            FeedbackText.Text = $"Monitoring reminder could not be shown: {exception.Message}";
        }
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
        {
            WindowContentSizing.Fit(this, RootPanel, 520, preservePosition: preservePosition,
                additionalWidth: SettingsSplitView.IsPaneOpen ? 520 : 0);
        }
        UpdateSettingsPaneWidth();
    }

    private void UpdateSettingsPaneWidth()
    {
        var scale = Math.Max(0.1, RootPanel.XamlRoot?.RasterizationScale ?? 1);
        SettingsSplitView.OpenPaneLength = AppWindow.ClientSize.Width / scale / 2;
    }

    private async void ToggleButton_Click(object sender, RoutedEventArgs args) =>
        await ToggleDuckingAsync();

    private async Task ToggleDuckingAsync()
    {
        if (_microphoneOperationPending) return;
        SetMicrophoneOperationPending(true);
        try
        {
            if (_engine.IsRunning)
            {
                await Task.Run(_engine.Stop);
            }
            else
            {
                await _licensing.RefreshAsync();
                if (!_licensing.CanUseApp)
                {
                    FeedbackText.Text = "An active Microsoft Store license is required. Check the license or buy VoiceDucker.";
                    return;
                }
                _engine.Start();
            }
        }
        catch (Exception exception)
        {
            FeedbackText.Text = $"Microphone operation failed: {exception.Message}";
        }
        finally
        {
            SetMicrophoneOperationPending(false);
        }
    }


    /// <summary>
    /// Updates capture controls together so overlapping start, stop and input changes cannot race.
    /// </summary>
    private void SetMicrophoneOperationPending(bool pending)
    {
        _microphoneOperationPending = pending;
        ToggleButton.IsEnabled = !pending;
        MicrophoneComboBox.IsEnabled = !pending;
    }

    /// <summary>
    /// Marshals worker status to the UI; no microphone or mixer work runs in this callback.
    /// </summary>
    private void OnStatusChanged(EngineStatus status)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            StatusText.Text = status.Message;
            ToggleButtonLabel.Text = status.Running ? "Disable" : "Enable";
            ToggleButtonIcon.Glyph = status.Running ? "\uE769" : "\uE768";
            SetMicLedActive(status.Running);
            if (status.Running && !string.IsNullOrWhiteSpace(status.MicrophoneName))
                MicrophoneNameText.Text = $"Microphone: {status.MicrophoneName}";
            var rms = status.Running && double.IsFinite(status.MicrophoneRms)
                ? Math.Clamp(status.MicrophoneRms, 0, 1) : 0;
            var microphoneDb = rms > 0 ? 20 * Math.Log10(rms) : double.NegativeInfinity;
            var meterFraction = Math.Clamp((microphoneDb + 60) / 60, 0, 1);
            MicrophoneLevelColumn.Width = new GridLength(meterFraction, GridUnitType.Star);
            MicrophoneEmptyColumn.Width = new GridLength(1 - meterFraction, GridUnitType.Star);
            _microphoneMeterRunning = status.Running;
            _microphoneDetectionActive = status.Speaking;
            _microphoneMeterRms = rms;
            UpdateMicrophoneThresholdHint();
            MicrophoneLevelText.Text = !status.Running ? "Off" : rms <= 0 ? "−∞ dBFS" : $"{microphoneDb:0.0} dBFS";

            VisualStateText.Text = !status.Running ? "MIC OFF" :
                status.Speaking && status.AffectedSessions > 0 ? "AUDIO LOWERING" :
                status.Speaking ? "VOICE DETECTED" : "LISTENING";
            try
            {
                _tray?.SetStatus(VisualStateText.Text);
            }
            catch (Exception exception)
            {
                FeedbackText.Text = $"Tray status could not be updated: {exception.Message}";
            }
            MixerImage.Opacity = status.Running ? 1 : 0.6;
            var level = status.Running ? status.MicrophoneLevel : 0;
            var scale = 1 + level * 0.035;
            MixerScale.ScaleX = scale;
            MixerScale.ScaleY = scale;
            MicGlow.Opacity = status.Speaking ? 0.28 + level * 0.55 : 0;

            var bars = new[] { Bar1, Bar2, Bar3, Bar4, Bar5 };
            var weights = new[] { 0.45, 0.75, 1.0, 0.7, 0.4 };
            for (var index = 0; index < bars.Length; index++)
            {
                bars[index].Height = 6 + level * 26 * weights[index];
                bars[index].Opacity = status.Running ? 0.5 + level * 0.5 : 0.25;
            }
        });
    }


    /// <summary>
    /// Gives both microphone illustrations the same repeating opacity timing.
    /// </summary>
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

    private void SetMicLedActive(bool active)
    {
        if (_micLedActive == active)
        {
            return;
        }

        _micLedActive = active;
        MicStatusBorder.Background = new SolidColorBrush(active
            ? Color.FromArgb(255, 6, 5, 7)
            : Color.FromArgb(255, 38, 59, 80));
        MicStatusBorder.BorderBrush = new SolidColorBrush(active
            ? Color.FromArgb(255, 116, 29, 39)
            : Color.FromArgb(255, 246, 162, 76));
        if (active)
        {
            var gradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            gradient.GradientStops.Add(new GradientStop
            {
                Color = Color.FromArgb(255, 126, 24, 34),
                Offset = 0
            });
            gradient.GradientStops.Add(new GradientStop
            {
                Color = Color.FromArgb(255, 247, 112, 106),
                Offset = 0.39
            });
            gradient.GradientStops.Add(new GradientStop
            {
                Color = Color.FromArgb(255, 168, 38, 45),
                Offset = 0.71
            });
            gradient.GradientStops.Add(new GradientStop
            {
                Color = Color.FromArgb(255, 74, 14, 23),
                Offset = 1
            });
            MicStatusIcon.Foreground = gradient;
        }
        else
        {
            MicStatusIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 246, 162, 76));
        }

        if (active)
        {
            _micLedPulse.Begin();
            _mixerPulse.Begin();
        }
        else
        {
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

    private void OnLicenseChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_lifetime.IsCancellationRequested) _ = UpdateLicenseUiAsync();
    });

    private async Task UpdateLicenseUiAsync()
    {
        if (_lifetime.IsCancellationRequested) return;
        if (_licensing.State == LicenseState.Trial) _licenseTimer.Start();
        else _licenseTimer.Stop();
        UpdateLicenseTitle();
        FooterPurchasePanel.Visibility = _licensing.IsStoreEdition && _licensing.State != LicenseState.Full
            ? Visibility.Visible : Visibility.Collapsed;
        FooterTrialText.Text = _licensing.State == LicenseState.Trial ? "You're using the trial version"
            : _licensing.State == LicenseState.Expired ? "Trial expired" : "Microsoft Store edition";
        LicensePanel.Visibility = _licensing.IsStoreEdition && _licensing.State is LicenseState.Checking or LicenseState.Unavailable
            ? Visibility.Visible : Visibility.Collapsed;
        LicenseStatusText.Text = _licensing.State switch
        {
            LicenseState.Trial => $"Free trial — {Math.Max(1, (int)Math.Ceiling((_licensing.Expires - DateTimeOffset.UtcNow).TotalDays))} days remaining. Ends {_licensing.Expires.LocalDateTime:d}. Purchase is optional; no automatic charge.",
            LicenseState.Expired => "Your trial or license has ended. Buy VoiceDucker to keep using it.",
            LicenseState.Checking => "Checking your Microsoft Store license…",
            LicenseState.Unavailable => "Your Microsoft Store license could not be checked. Please retry.",
            _ => string.Empty
        };
        if (!_licensing.CanUseApp && _engine.IsRunning)
        {
            try { await Task.Run(_engine.Stop); }
            catch (Exception exception) { FeedbackText.Text = $"Audio restoration failed: {exception.Message}"; }
        }
        if (_lifetime.IsCancellationRequested) return;
        if (RootPanel.IsLoaded)
            FitMainWindow();
        if (_licensing.State == LicenseState.Expired && _licensing.IsTrialLicense && !_trialExpiryShown)
        {
            _trialExpiryShown = true;
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
                if (result == ContentDialogResult.Primary)
                    await Windows.System.Launcher.LaunchUriAsync(new Uri($"ms-windows-store://pdp/?productid={StoreLicenseService.StoreId}"));
                else if (result == ContentDialogResult.Secondary)
                    await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/umbertotechnopreneur/VoiceDucker"));
            }
            catch (Exception) { FeedbackText.Text = "Your trial has ended. Use Purchase to open Microsoft Store, or visit VoiceDucker on GitHub."; }
        }
    }

    private void UpdateLicenseTitle()
    {
        var title = LicensePolicy.WindowTitle("VoiceDucker", _licensing.State, _licensing.Expires, DateTimeOffset.UtcNow);
        if (Title != title) Title = title;
    }

    private async void CheckLicense_Click(object sender, RoutedEventArgs args) => await _licensing.RefreshAsync();

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
        // Keep links visible while the user is interacting with them, including by keyboard.
        var focusedElement = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(RootPanel.XamlRoot) as DependencyObject;
        while (focusedElement is not null)
        {
            if (focusedElement == FooterLinks)
                return;
            focusedElement = VisualTreeHelper.GetParent(focusedElement);
        }
        if (_footerPointerOver)
            return;
        _footerTimer.Stop();
        _footerFade.Begin();
        // Preserve the footer's layout space so the interface does not jump after fading.
    }

    private void AboutButton_Click(object sender, RoutedEventArgs args) => ShowAbout();

    private void ShowAbout()
    {
        if (_aboutWindow is not null)
        {
            _aboutWindow.Activate();
            return;
        }
        _aboutWindow = new AboutWindow();
        _aboutWindow.Closed += (_, _) => _aboutWindow = null;
        _aboutWindow.Activate();
    }

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
            FeedbackText.Text = $"Could not minimize to tray: {exception.Message}";
            _tray?.ShowWindow();
        }
    }

    private void ExitApplication()
    {
        _windowPlacement.SaveNow();
        _allowClose = true;
        Close();
    }

    private void OnStreamTimerTick(object? sender, object args) => RefreshStreamRows();

    private void OnClosed(object sender, WindowEventArgs args)
    {
        void Release(string resource, Action cleanup)
        {
            try { cleanup(); }
            catch (Exception exception)
            {
                AudioDiagnostics.Write(() => $"shutdown-failed resource={resource} error={exception.Message}");
            }
        }

        _windowPlacement.SizeChanged -= UpdateSettingsPaneWidth;
        Release("window-placement", _windowPlacement.Dispose);
        Release("pending-ui-work", _lifetime.Cancel);
        _licenseTimer.Stop();
        _licensing.Changed -= OnLicenseChanged;
        Release("store-license", _licensing.Dispose);
        _footerTimer.Stop();
        _footerFade.Stop();
        _micLedPulse.Stop();
        _mixerPulse.Stop();
        _streamTimer?.Stop();
        if (_streamTimer is not null) _streamTimer.Tick -= OnStreamTimerTick;
        _streamTimer = null;
        Release("volume-notifications", () => _playbackMonitor?.Dispose());
        _playbackMonitor = null;
        _engine.StatusChanged -= OnStatusChanged;
        AppWindow.Closing -= OnWindowClosing;
        Release("tray", () => _tray?.Dispose());
        Release("about-window", () => _aboutWindow?.Close());
        Release("close-choice", () => _closeChoice?.Close());
        Release("audio-worker", _engine.Dispose);
        Release("ui-lifetime", _lifetime.Dispose);
        AudioDiagnostics.Write(() => "application-shutdown cleanup=finished");
    }
}
