/* VBWR B
 * Project: VoiceDucker
 * Repository: https://github.com/umbertotechnopreneur/VoiceDucker
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 * VibeWare initiative: Human intent. AI implementation. Accountable human review.
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
using VoiceDucker.Audio;
using Windows.Graphics;

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

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(520, 620));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        _settings = SettingsStore.Load(out var settingsError);
        _engine = new DuckingEngine(_settings);
        ReductionBox.Value = _settings.ReductionPercent;
        FadeDownBox.Value = _settings.FadeDownMilliseconds;
        FadeUpBox.Value = _settings.FadeUpMilliseconds;
        _settingsReady = true;
        FeedbackText.Text = settingsError ?? string.Empty;

        try
        {
            StartupCheckBox.IsChecked = StartupRegistration.IsEnabled();
        }
        catch (Exception exception)
        {
            FeedbackText.Text = $"Windows startup status could not be read: {exception.Message}";
        }

        _engine.StatusChanged += OnStatusChanged;
        AppWindow.Closing += OnWindowClosing;
        RootPanel.Loaded += OnRootLoaded;
        Closed += OnClosed;
    }

    private void OnRootLoaded(object sender, RoutedEventArgs args)
    {
        RootPanel.Loaded -= OnRootLoaded;
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

        var values = new[] { ReductionBox.Value, FadeDownBox.Value, FadeUpBox.Value };
        if (values.Any(value => !double.IsFinite(value) || value != Math.Round(value)))
        {
            FeedbackText.Text = "Enter whole numbers within each setting's range.";
            return;
        }

        var next = new DuckingSettings((int)values[0], (int)values[1], (int)values[2]);
        if (!next.IsValid)
        {
            FeedbackText.Text = "Reduction must be 0–100%; fade times must be 0–5000 ms.";
            return;
        }

        _settings = next;
        _engine.UpdateSettings(next);
        try
        {
            SettingsStore.Save(next);
            FeedbackText.Text = string.Empty;
        }
        catch (Exception exception)
        {
            FeedbackText.Text = $"Settings are active but could not be saved: {exception.Message}";
        }
    }

    private void StartupCheckBox_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            StartupRegistration.SetEnabled(StartupCheckBox.IsChecked == true);
            FeedbackText.Text = string.Empty;
        }
        catch (Exception exception)
        {
            FeedbackText.Text = $"Windows startup could not be changed: {exception.Message}";
            try
            {
                StartupCheckBox.IsChecked = StartupRegistration.IsEnabled();
            }
            catch
            {
                StartupCheckBox.IsChecked = false;
            }
        }
    }

    private void OnStatusChanged(EngineStatus status)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            StatusText.Text = status.Message;
            ToggleButtonLabel.Text = status.Running ? "Disable" : "Enable";
            ToggleButtonIcon.Glyph = status.Running ? "\uE769" : "\uE768";

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
        var position = AppWindow.Position;
        var size = AppWindow.Size;
        _closeChoice.AppWindow.Move(new PointInt32(
            position.X + (size.Width - 440) / 2,
            position.Y + (size.Height - 235) / 2));
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
        _engine.StatusChanged -= OnStatusChanged;
        AppWindow.Closing -= OnWindowClosing;
        _tray?.Dispose();
        _aboutWindow?.Close();
        _closeChoice?.Close();
        _engine.Dispose();
    }
}
