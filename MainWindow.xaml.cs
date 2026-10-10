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
using VoiceDucker.Visuals;
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
    private readonly AppAudioVisualizers _audioVisualizers = new();
    private DispatcherTimer? _visualizerTimer;

    public MainWindow()
    {
        InitializeComponent();
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
        ReductionBox.Value = _settings.ReductionPercent;
        FadeDownBox.Value = _settings.FadeDownMilliseconds;
        FadeUpBox.Value = _settings.FadeUpMilliseconds;
        OtherSourcesCheckBox.IsChecked = _settings.IncludeOtherSources;
        ShowVisualizersCheckBox.IsChecked = _settings.ShowVisualizers;
        RefreshHiddenApps();
        _settingsReady = true;
        FeedbackText.Text = settingsError ?? string.Empty;

        StartupCheckBox.IsEnabled = false;

        _engine.StatusChanged += OnStatusChanged;
        AppWindow.Closing += OnWindowClosing;
        AppWindow.Changed += OnAppWindowChanged;
        RootPanel.Loaded += OnRootLoaded;
        Closed += OnClosed;
    }

    private async void OnRootLoaded(object sender, RoutedEventArgs args)
    {
        RootPanel.Loaded -= OnRootLoaded;
        WindowContentSizing.Fit(this, RootPanel, 520);
        try
        {
            _playbackMonitor = new PlaybackMonitor();
            _streamTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _streamTimer.Tick += (_, _) => RefreshStreamRows();
            RefreshStreamRows();
            _streamTimer.Start();
            _visualizerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _visualizerTimer.Tick += (_, _) => RefreshVisualizers();
            SynchronizeVisualizers();
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
        ToggleButton.IsEnabled = false;
        try
        {
            if (_engine.IsRunning)
            {
                await Task.Run(_engine.Stop);
            }
            else
            {
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
            FeedbackText.Text = "Reduction: 0–100%; fade down: 0–5000 ms; fade back: 0–30000 ms.";
            return;
        }

        var next = sender == ReductionBox
            ? _settings with { ReductionPercent = (int)value }
            : sender == FadeDownBox
                ? _settings with { FadeDownMilliseconds = (int)value }
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

    private void ShowVisualizersCheckBox_Click(object sender, RoutedEventArgs args)
    {
        if (!_settingsReady)
        {
            return;
        }

        _settings = _settings with { ShowVisualizers = ShowVisualizersCheckBox.IsChecked == true };
        _engine.UpdateSettings(_settings);
        foreach (var row in _streamRows.Values)
        {
            row.Visualizer.Visibility = _settings.ShowVisualizers ? Visibility.Visible : Visibility.Collapsed;
        }
        SynchronizeVisualizers();
        SaveSettings();
        WindowContentSizing.Fit(this, RootPanel, 520, preservePosition: true);
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

                row.Muted = stream.Muted;
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
        SynchronizeVisualizers();
        if (layoutChanged)
        {
            WindowContentSizing.Fit(this, RootPanel, 520, preservePosition: true);
        }
    }

    private void SynchronizeVisualizers()
    {
        var visible = _settings.ShowVisualizers && AppWindow.IsVisible &&
                      AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized };
        _audioVisualizers.Synchronize(visible ? _streamRows.Values.Select(row => row.ProcessId) : []);
        if (visible && _streamRows.Count > 0) _visualizerTimer?.Start();
        else _visualizerTimer?.Stop();
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidVisibilityChange || args.DidSizeChange || args.DidPresenterChange)
            SynchronizeVisualizers();
    }

    private void RefreshVisualizers()
    {
        foreach (var row in _streamRows.Values)
            row.Visualizer.Update(_audioVisualizers.GetFrame(row.ProcessId), row.Muted);
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
            FontFamily = CassetteVisualizer.RetroFont,
            FontSize = 10,
            Foreground = CassetteVisualizer.Brush(0xE8E2CE),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        var percentage = new TextBlock
        {
            Text = $"{Math.Round(stream.Volume * 100):0}%",
            FontFamily = CassetteVisualizer.RetroFont,
            FontSize = 10,
            Foreground = CassetteVisualizer.Brush(0xE8E2CE),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var title = new Grid { ColumnSpacing = 8 };
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(name, 1);
        Grid.SetColumn(percentage, 2);
        title.Children.Add(name);
        title.Children.Add(percentage);
        var remove = new Button
        {
            Content = "Remove", FontFamily = CassetteVisualizer.RetroFont, FontSize = 7,
            Padding = new Thickness(8, 5, 8, 5), MinWidth = 0, MinHeight = 24,
            Foreground = CassetteVisualizer.Brush(0xE8E2CE),
            Background = CassetteVisualizer.Brush(0x171A17),
            BorderBrush = CassetteVisualizer.Brush(0x686B60),
            VerticalAlignment = VerticalAlignment.Center
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, $"Remove {stream.Name}");
        ToolTipService.SetToolTip(remove,
            "Hide this app and exclude it from lowering. Restore it under Hidden apps.");
        remove.Click += (_, _) => SetProcessHidden(stream.Name, true);
        Grid.SetColumn(remove, 3);
        title.Children.Add(remove);
        var icon = new Image { Width = 24, Height = 24, Stretch = Stretch.Uniform };
        var iconPlaceholder = new FontIcon
        {
            Glyph = "\uE80A",
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 16,
            Foreground = CassetteVisualizer.Brush(0xE8E2CE),
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
        slider.Resources["SliderTrackFill"] = CassetteVisualizer.Brush(0x111411);
        slider.Resources["SliderTrackValueFill"] = CassetteVisualizer.Brush(0xD3CEBA);
        slider.Resources["SliderThumbBackground"] = CassetteVisualizer.Brush(0xE8E2CE);
        slider.Resources["SliderThumbBackgroundPointerOver"] = CassetteVisualizer.Brush(0xFFF3D0);
        slider.Resources["SliderThumbBackgroundPressed"] = CassetteVisualizer.Brush(0xC6B98E);
        var visualizer = new CassetteVisualizer
        {
            Visibility = _settings.ShowVisualizers ? Visibility.Visible : Visibility.Collapsed
        };
        ToolTipService.SetToolTip(visualizer,
            "Stereo VU: 0 = -18 dBFS RMS. Spectrum: 40 Hz to 16 kHz. App process audio across outputs.");
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(title);
        panel.Children.Add(slider);
        panel.Children.Add(visualizer);
        var card = new Border
        {
            Child = panel,
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = CassetteVisualizer.Brush(0x4B4E48),
            Background = CassetteVisualizer.Brush(0x272B28)
        };
        var row = new StreamRow(card, slider, visualizer, percentage, icon, stream.ProcessId) { Muted = stream.Muted };
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

    private sealed class StreamRow(Border card, Slider slider, CassetteVisualizer visualizer,
        TextBlock percentage, Image icon, int processId)
    {
        public Border Card { get; } = card;
        public Slider Slider { get; } = slider;
        public CassetteVisualizer Visualizer { get; } = visualizer;
        public TextBlock Percentage { get; } = percentage;
        public Image Icon { get; } = icon;
        public bool Interacting { get; set; }
        public int ProcessId { get; } = processId;
        public bool Muted { get; set; }
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
        _micLedPulse.Stop();
        _mixerPulse.Stop();
        _streamTimer?.Stop();
        _visualizerTimer?.Stop();
        _audioVisualizers.Dispose();
        _playbackMonitor?.Dispose();
        _engine.StatusChanged -= OnStatusChanged;
        AppWindow.Closing -= OnWindowClosing;
        AppWindow.Changed -= OnAppWindowChanged;
        _tray?.Dispose();
        _aboutWindow?.Close();
        _closeChoice?.Close();
        _engine.Dispose();
    }
}
