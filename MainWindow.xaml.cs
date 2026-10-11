/* VBWR B
 * Project: VoiceDucker
 * Repository: https://github.com/umbertotechnopreneur/VoiceDucker
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 * VibeWare initiative: Human intent. AI implementation. Accountable human review.
 * Created with AI: OpenAI Codex assisted the initial implementation; Git history
 * Modified with AI: OpenAI Codex; audio settings, reactive mixer, tray and About, 2026-10-08
 * Human guidance: Umberto Giacobbi defined the purpose and audio behavior
 * Evidence: Git history
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT
 * VBWR E */

using System.Globalization;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using VoiceDucker.Audio;
using VoiceDucker.Licensing;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage.Streams;
using Windows.UI;

namespace VoiceDucker;

public sealed partial class MainWindow : Window
{
    private readonly DuckingEngine _engine;
    private DuckingSettings _settings;
    private TrayIconService? _tray;
    private AboutWindow? _aboutWindow;
    private CloseChoiceWindow? _closeChoice;
    private bool _settingsReady;
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
    private readonly DispatcherTimer _footerTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly Storyboard _footerFade = new();
    private bool _footerPointerOver;
    private readonly StoreLicenseService _licensing;
    private readonly DispatcherTimer _licenseTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _trialExpiryShown;

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

        var pulse = new DoubleAnimation
        {
            From = 0.15,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(900)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(pulse, MicStatusIcon);
        Storyboard.SetTargetProperty(pulse, "Opacity");
        _micLedPulse.Children.Add(pulse);

        var mixerPulse = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(900)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(mixerPulse, MixerOnImage);
        Storyboard.SetTargetProperty(mixerPulse, "Opacity");
        _mixerPulse.Children.Add(mixerPulse);

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
        SensitivitySlider.Value = _settings.MicrophoneGainDecibels;
        UpdateSensitivityLabel();
        var thresholdDb = 20 * Math.Log10(VoiceGate.Threshold);
        var thresholdPosition = Math.Clamp((thresholdDb + 60) / 60, 0, 1);
        ThresholdBeforeColumn.Width = new GridLength(thresholdPosition, GridUnitType.Star);
        ThresholdAfterColumn.Width = new GridLength(1 - thresholdPosition, GridUnitType.Star);
        MicrophoneThresholdText.Text = $"Threshold {thresholdDb:0.0} dBFS";
        RefreshHiddenApps();
        _settingsReady = true;
        FeedbackText.Text = settingsError ?? string.Empty;
        RefreshMicrophoneChoices();

        StartupCheckBox.IsEnabled = false;

        _engine.StatusChanged += OnStatusChanged;
        AppWindow.Closing += OnWindowClosing;
        RootPanel.Loaded += OnRootLoaded;
        Closed += OnClosed;
    }

    private async void OnRootLoaded(object sender, RoutedEventArgs args)
    {
        RootPanel.Loaded -= OnRootLoaded;
        WindowContentSizing.Fit(this, RootPanel, 520);
        RevealFooter();
        if (_licensing.IsStoreEdition)
        {
            await UpdateLicenseUiAsync();
            _ = _licensing.RefreshAsync();
        }
        try
        {
            _playbackMonitor = new PlaybackMonitor();
            _streamTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _streamTimer.Tick += (_, _) => RefreshStreamRows();
            RefreshStreamRows();
            _streamTimer.Start();

        }
        catch (Exception exception)
        {
            FeedbackText.Text = $"Audio streams could not be listed: {exception.Message}";
        }
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
            await StartupRegistration.InitializeDefaultAsync(SettingsStore.HasSavedSettings);
            StartupCheckBox.IsChecked = await StartupRegistration.IsEnabledAsync();
        }
        catch (Exception exception)
        {
            FeedbackText.Text = $"Windows startup status could not be read: {exception.Message}";
        }
        finally
        {
            StartupCheckBox.IsEnabled = true;
        }
    }

    private void AdvancedOptionsExpander_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (RootPanel.IsLoaded && args.NewSize.Height != args.PreviousSize.Height)
        {
            // Fit after the expander has laid out its expanded or collapsed content.
            DispatcherQueue.TryEnqueue(() =>
                WindowContentSizing.Fit(this, RootPanel, 520, preservePosition: true));
        }
    }

    private async void ToggleButton_Click(object sender, RoutedEventArgs args) =>
        await ToggleDuckingAsync();

    private async Task ToggleDuckingAsync()
    {
        if (_microphoneOperationPending) return;
        _microphoneOperationPending = true;
        ToggleButton.IsEnabled = false;
        MicrophoneComboBox.IsEnabled = false;
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
            ToggleButton.IsEnabled = true;
            MicrophoneComboBox.IsEnabled = true;
            _microphoneOperationPending = false;
        }
    }

    private void SettingBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_settingsReady)
        {
            return;
        }

        ApplyNumericSetting(sender, args.NewValue);
    }

    private void SettingBox_Loaded(object sender, RoutedEventArgs args)
    {
        var numberBox = (NumberBox)sender;
        numberBox.ApplyTemplate();
        AttachTextChanged(numberBox, numberBox);
    }

    private void AttachTextChanged(NumberBox numberBox, DependencyObject element)
    {
        if (element is TextBox input)
        {
            input.Tag = numberBox;
            input.TextChanged -= SettingText_TextChanged;
            input.TextChanged += SettingText_TextChanged;
            return;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        {
            AttachTextChanged(numberBox, VisualTreeHelper.GetChild(element, index));
        }
    }

    private void SettingText_TextChanged(object sender, TextChangedEventArgs args)
    {
        if (!_settingsReady || sender is not TextBox input || input.Tag is not NumberBox numberBox)
        {
            return;
        }

        // NumberBox.Value changes only on commit; save valid edits before focus leaves the field.
        if (!double.TryParse(input.Text, NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.CurrentCulture, out var value))
        {
            FeedbackText.Text = "Enter whole numbers within each setting's range.";
            return;
        }

        ApplyNumericSetting(numberBox, value);
    }

    private void ApplyNumericSetting(NumberBox sender, double value)
    {
        if (!double.IsFinite(value) || value != Math.Round(value))
        {
            FeedbackText.Text = "Enter whole numbers within each setting's range.";
            return;
        }

        if (value < sender.Minimum || value > sender.Maximum)
        {
            FeedbackText.Text = "Reduction: 0–100%; fade down: 0–5000 ms; wait: 0–10000 ms; fade back: 0–30000 ms.";
            return;
        }

        var next = sender == ReductionBox
            ? _settings with { ReductionPercent = (int)value }
            : sender == FadeDownBox
                ? _settings with { FadeDownMilliseconds = (int)value }
                : sender == RestoreDelayBox
                    ? _settings with { RestoreDelayMilliseconds = (int)value }
                    : _settings with { FadeUpMilliseconds = (int)value };
        _settings = next;
        _engine.UpdateSettings(next);
        SaveSettings();
    }

    private void OtherSourcesCheckBox_Click(object sender, RoutedEventArgs args)
    {
        if (!_settingsReady)
        {
            return;
        }

        _settings = _settings with { IncludeOtherSources = OtherSourcesCheckBox.IsChecked == true };
        _engine.UpdateSettings(_settings);
        SaveSettings();
    }

    private void SaveSettings()
    {
        try
        {
            SettingsStore.Save(_settings);
            FeedbackText.Text = string.Empty;
        }
        catch (Exception exception)
        {
            FeedbackText.Text = $"Settings are active but could not be saved: {exception.Message}";
        }
    }

    private void SensitivitySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs args)
    {
        UpdateSensitivityLabel();
        if (!_settingsReady) return;
        _settings = _settings with { MicrophoneGainDecibels = (int)Math.Round(args.NewValue) };
        _engine.UpdateSettings(_settings);
        SaveSettings();
    }

    private void UpdateSensitivityLabel()
    {
        if (SensitivityValueText is not null && SensitivitySlider is not null)
            SensitivityValueText.Text = $"{SensitivitySlider.Value:+0;-0;0} dB";
    }

    private void SensitivityReset_Click(object sender, RoutedEventArgs args) =>
        SensitivitySlider.Value = 0;

    private void MicrophoneComboBox_DropDownOpened(object sender, object args) =>
        RefreshMicrophoneChoices();

    private void RefreshMicrophoneChoices()
    {
        _refreshingMicrophones = true;
        try
        {
            var choices = MicrophoneDevices.GetChoices().ToList();
            var selected = choices.FirstOrDefault(choice => choice.Id == _settings.MicrophoneDeviceId);
            if (selected is null)
            {
                // Keep a disconnected choice visible; never silently switch to another input.
                selected = new MicrophoneChoice(_settings.MicrophoneDeviceId, "Unavailable microphone — choose another input");
                choices.Add(selected);
            }
            MicrophoneComboBox.ItemsSource = choices;
            MicrophoneComboBox.SelectedItem = selected;
            if (!_engine.IsRunning)
                MicrophoneNameText.Text = $"Microphone: {selected.Name}";
        }
        catch (Exception exception)
        {
            MicrophoneNameText.Text = "Microphone: device list unavailable";
            FeedbackText.Text = $"Microphones could not be listed: {exception.Message}";
        }
        finally
        {
            _refreshingMicrophones = false;
        }
    }

    private async void MicrophoneComboBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_settingsReady || _refreshingMicrophones || _microphoneOperationPending ||
            MicrophoneComboBox.SelectedItem is not MicrophoneChoice choice ||
            choice.Id == _settings.MicrophoneDeviceId) return;

        _microphoneOperationPending = true;
        ToggleButton.IsEnabled = false;
        MicrophoneComboBox.IsEnabled = false;
        var restart = _engine.IsRunning;
        try
        {
            if (restart)
            {
                await Task.Run(_engine.Stop);
                if (_engine.IsRunning)
                    throw new InvalidOperationException("The previous microphone has not stopped. The selection was kept unchanged.");
            }
            _settings = _settings with { MicrophoneDeviceId = choice.Id };
            _engine.UpdateSettings(_settings);
            SaveSettings();
            MicrophoneNameText.Text = $"Microphone: {choice.Name}";
            if (restart && _licensing.CanUseApp) _engine.Start();
        }
        catch (Exception exception)
        {
            RefreshMicrophoneChoices();
            FeedbackText.Text = $"Microphone could not be changed: {exception.Message}";
        }
        finally
        {
            ToggleButton.IsEnabled = true;
            MicrophoneComboBox.IsEnabled = true;
            _microphoneOperationPending = false;
        }
    }

    private async void StartupCheckBox_Click(object sender, RoutedEventArgs args)
    {
        StartupCheckBox.IsEnabled = false;
        try
        {
            await StartupRegistration.SetEnabledAsync(StartupCheckBox.IsChecked == true);
            StartupRegistration.SavePreferenceMarker();
            FeedbackText.Text = string.Empty;
        }
        catch (Exception exception)
        {
            FeedbackText.Text = $"Windows startup could not be changed: {exception.Message}";
            try
            {
                StartupCheckBox.IsChecked = await StartupRegistration.IsEnabledAsync();
            }
            catch
            {
                StartupCheckBox.IsChecked = false;
            }
        }
        finally
        {
            StartupCheckBox.IsEnabled = true;
        }
    }

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
            MicrophoneLevelHint.Text = !status.Running
                ? "Press Enable to see your microphone level."
                : rms >= VoiceGate.Threshold
                    ? "Above threshold — sound detected"
                    : "Below threshold";
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

    private void RefreshStreamRows()
    {
        if (_playbackMonitor is null)
        {
            return;
        }

        IReadOnlyList<PlaybackStream> streams;
        try
        {
            streams = _playbackMonitor.Read()
                .Where(stream => !_settings.IsProcessExcluded(stream.Name)).ToArray();
        }
        catch (Exception exception)
        {
            FeedbackText.Text = $"Audio streams could not be refreshed: {exception.Message}";
            return;
        }

        var present = streams.Select(stream => stream.Key).ToHashSet(StringComparer.Ordinal);
        var layoutChanged = false;
        foreach (var key in _streamRows.Keys.Where(key => !present.Contains(key)).ToArray())
        {
            StreamRows.Children.Remove(_streamRows[key].Card);
            _streamRows.Remove(key);
            layoutChanged = true;
        }

        _refreshingStreamRows = true;
        try
        {
            foreach (var stream in streams.OrderByDescending(stream => stream.IsSpotify)
                         .ThenBy(stream => stream.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!_streamRows.TryGetValue(stream.Key, out var row))
                {
                    row = CreateStreamRow(stream);
                    _streamRows.Add(stream.Key, row);
                    StreamRows.Children.Add(row.Card);
                    _ = LoadStreamIconAsync(stream.ProcessId, stream.Key, row.Icon);
                    layoutChanged = true;
                }

                var peak = stream.Muted ? 0 : Math.Clamp(stream.Peak, 0, 1);
                row.Meter.Value = peak <= 0 ? 0 : Math.Clamp((20 * Math.Log10(peak) + 60) / 60 * 100, 0, 100);
                row.Slider.IsEnabled = !stream.Muted;
                if (!row.Interacting && row.Slider.FocusState == FocusState.Unfocused)
                {
                    row.Slider.Value = Math.Clamp(stream.Volume * 100, 0, 100);
                    row.Percentage.Text = stream.Muted
                        ? "MUTED"
                        : $"{Math.Round(stream.Volume * 100):0}%";
                }
            }
        }
        finally
        {
            _refreshingStreamRows = false;
        }

        NoStreamsText.Visibility = streams.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (layoutChanged)
        {
            WindowContentSizing.Fit(this, RootPanel, 520, preservePosition: true);
        }
    }

    private async Task LoadStreamIconAsync(int processId, string key, Image icon)
    {
        try
        {
            var png = await Task.Run(() => ProcessIcon.ReadPng(processId));
            if (png is null)
            {
                return;
            }

            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(png);
                await writer.StoreAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            var source = new BitmapImage();
            await source.SetSourceAsync(stream);
            if (_streamRows.TryGetValue(key, out var row) && ReferenceEquals(row.Icon, icon))
            {
                icon.Source = source;
            }
        }
        catch (Exception)
        {
            // Protected or short-lived processes keep the generic app glyph.
        }
    }

    private StreamRow CreateStreamRow(PlaybackStream stream)
    {
        var name = new TextBlock
        {
            Text = stream.IsSpotify ? "SPOTIFY" : stream.Name.ToUpperInvariant(),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        var percentage = new TextBlock
        {
            Text = $"{Math.Round(stream.Volume * 100):0}%",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var title = new Grid { ColumnSpacing = 8 };
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(name, 1);
        Grid.SetColumn(percentage, 2);
        title.Children.Add(name);
        title.Children.Add(percentage);
        var icon = new Image { Width = 24, Height = 24, Stretch = Stretch.Uniform };
        var iconPlaceholder = new FontIcon
        {
            Glyph = "\uE80A",
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 16,
            Opacity = 0.55
        };
        var iconLayer = new Grid { Width = 24, Height = 24, Margin = new Thickness(0, 0, 8, 0) };
        iconLayer.Children.Add(iconPlaceholder);
        iconLayer.Children.Add(icon);
        title.Children.Add(iconLayer);

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            StepFrequency = 1,
            Value = Math.Clamp(stream.Volume * 100, 0, 100),
            IsEnabled = !stream.Muted
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider, $"{stream.Name} volume");
        // Overlay the session peak on the slider track; volume remains independently draggable.
        var meter = new ProgressBar
        {
            Minimum = 0, Maximum = 100, Value = 0, Height = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0), IsHitTestVisible = false,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 246, 162, 76)),
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(meter, $"{stream.Name} audio level");
        var volumeTrack = new Grid();
        volumeTrack.Children.Add(slider);
        volumeTrack.Children.Add(meter);
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(title);
        panel.Children.Add(volumeTrack);
        var row = new StreamRow(panel, slider, meter, percentage, icon);
        slider.PointerPressed += (_, _) => row.Interacting = true;
        slider.PointerReleased += (_, _) => row.Interacting = false;
        slider.PointerCanceled += (_, _) => row.Interacting = false;
        slider.ValueChanged += (_, args) =>
        {
            if (_refreshingStreamRows || _playbackMonitor is null)
            {
                return;
            }

            percentage.Text = $"{Math.Round(args.NewValue):0}%";
            try
            {
                if (!_playbackMonitor.SetVolume(stream.Key, (float)(args.NewValue / 100)))
                {
                    FeedbackText.Text = "This audio stream has ended.";
                }
            }
            catch (Exception exception)
            {
                FeedbackText.Text = $"Volume could not be changed: {exception.Message}";
            }
        };
        return row;
    }

    private void SetProcessHidden(string processName, bool hidden)
    {
        var normalized = PlaybackIdentity.NormalizeProcessName(processName);
        var names = _settings.BlacklistedProcesses.Where(name =>
            !PlaybackIdentity.NormalizeProcessName(name).Equals(normalized, StringComparison.OrdinalIgnoreCase));
        _settings = _settings with
        {
            BlacklistedProcesses = (hidden ? names.Append(normalized) : names)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
        _engine.UpdateSettings(_settings);
        SaveSettings();
        RefreshHiddenApps();
        RefreshStreamRows();
    }

    private void RefreshHiddenApps()
    {
        HiddenAppsButton.Visibility = _settings.BlacklistedProcesses.Length > 0
            ? Visibility.Visible : Visibility.Collapsed;
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = "Hidden apps are excluded from the list and automatic volume lowering.",
            TextWrapping = TextWrapping.Wrap, MaxWidth = 280
        });
        foreach (var name in _settings.BlacklistedProcesses.Order(StringComparer.OrdinalIgnoreCase))
        {
            var restore = new Button { Content = $"Restore {name}" };
            restore.Click += (_, _) =>
            {
                HiddenAppsButton.Flyout.Hide();
                SetProcessHidden(name, false);
            };
            panel.Children.Add(restore);
        }
        HiddenAppsButton.Flyout = new Flyout { Content = panel };
    }

    private sealed class StreamRow(StackPanel card, Slider slider, ProgressBar meter,
        TextBlock percentage, Image icon)
    {
        public StackPanel Card { get; } = card;
        public Slider Slider { get; } = slider;
        public ProgressBar Meter { get; } = meter;
        public TextBlock Percentage { get; } = percentage;
        public Image Icon { get; } = icon;
        public bool Interacting { get; set; }
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
                Color = Color.FromArgb(255, 126, 24, 34), Offset = 0
            });
            gradient.GradientStops.Add(new GradientStop
            {
                Color = Color.FromArgb(255, 247, 112, 106), Offset = 0.39
            });
            gradient.GradientStops.Add(new GradientStop
            {
                Color = Color.FromArgb(255, 168, 38, 45), Offset = 0.71
            });
            gradient.GradientStops.Add(new GradientStop
            {
                Color = Color.FromArgb(255, 74, 14, 23), Offset = 1
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

    private void OnLicenseChanged() => DispatcherQueue.TryEnqueue(() => _ = UpdateLicenseUiAsync());

    private async Task UpdateLicenseUiAsync()
    {
        if (_licensing.State == LicenseState.Trial) _licenseTimer.Start();
        else _licenseTimer.Stop();
        UpdateLicenseTitle();
        FooterPurchaseButton.Visibility = _licensing.IsStoreEdition && _licensing.State != LicenseState.Full
            ? Visibility.Visible : Visibility.Collapsed;
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
        if (RootPanel.IsLoaded)
            WindowContentSizing.Fit(this, RootPanel, 520, preservePosition: true);
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
        _allowClose = true;
        Close();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _licenseTimer.Stop();
        _licensing.Changed -= OnLicenseChanged;
        _licensing.Dispose();
        _footerTimer.Stop();
        _footerFade.Stop();
        _micLedPulse.Stop();
        _mixerPulse.Stop();
        _streamTimer?.Stop();
        _playbackMonitor?.Dispose();
        _engine.StatusChanged -= OnStatusChanged;
        AppWindow.Closing -= OnWindowClosing;
        _tray?.Dispose();
        _aboutWindow?.Close();
        _closeChoice?.Close();
        _engine.Dispose();
    }
}
