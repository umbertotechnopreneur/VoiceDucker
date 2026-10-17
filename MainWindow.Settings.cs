// VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.

using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VoiceDucker.Audio;

namespace VoiceDucker;

// Settings editing, persistence feedback and microphone selection for the main window.
public sealed partial class MainWindow
{
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
            FeedbackText.Text = "Hysteresis: 0–12 dB; reduction: 0–100%; fade down: 0–5000 ms; wait: 0–10000 ms; fade back: 0–30000 ms.";
            return;
        }

        var integerValue = (int)value;
        var next = sender switch
        {
            var box when box == HysteresisBox => _settings with { HysteresisDecibels = integerValue },
            var box when box == ReductionBox => _settings with { ReductionPercent = integerValue },
            var box when box == FadeDownBox => _settings with { FadeDownMilliseconds = integerValue },
            var box when box == RestoreDelayBox => _settings with { RestoreDelayMilliseconds = integerValue },
            var box when box == FadeUpBox => _settings with { FadeUpMilliseconds = integerValue },
            _ => throw new InvalidOperationException("An unknown numeric setting control was used.")
        };
        _settings = next;
        UpdateThresholdDisplay();
        ApplyAndSaveSettings();
    }

    private void OtherSourcesCheckBox_Click(object sender, RoutedEventArgs args)
    {
        if (!_settingsReady)
        {
            return;
        }

        _settings = _settings with { IncludeOtherSources = OtherSourcesCheckBox.IsChecked == true };
        ApplyAndSaveSettings();
    }

    /// <summary>
    /// Applies a validated edit immediately, then attempts to persist it.
    /// A disk failure is reported without undoing the active in-memory choice.
    /// </summary>
    private void ApplyAndSaveSettings()
    {
        _engine.UpdateSettings(_settings);

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
        ApplyAndSaveSettings();
    }

    private void UpdateSensitivityLabel()
    {
        if (SensitivityValueText is not null && SensitivitySlider is not null)
            SensitivityValueText.Text = $"{SensitivitySlider.Value:+0;-0;0} dB";
    }

    private void SensitivityReset_Click(object sender, RoutedEventArgs args) =>
        SensitivitySlider.Value = 0;

    private void ThresholdSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs args)
    {
        if (!_settingsReady) return;
        _settings = _settings with { ThresholdDecibels = args.NewValue };
        UpdateThresholdDisplay();
        ApplyAndSaveSettings();
    }

    private void UpdateThresholdDisplay()
    {
        var thresholdDb = _settings.ThresholdDecibels;
        var releaseThresholdDb = thresholdDb - _settings.HysteresisDecibels;
        var position = Math.Clamp((thresholdDb + 60) / 60, 0, 1);
        ThresholdBeforeColumn.Width = new GridLength(position, GridUnitType.Star);
        ThresholdAfterColumn.Width = new GridLength(1 - position, GridUnitType.Star);
        MicrophoneThresholdText.Text = $"Threshold {thresholdDb:0.0} dBFS";
        ThresholdValueText.Text = $"{thresholdDb:0.0} dBFS";
        ReleaseThresholdText.Text = $"Release below {releaseThresholdDb:0.0} dBFS, then wait before fade back.";
        UpdateMicrophoneThresholdHint();
    }

    /// <summary>
    /// Explains whether the level, hysteresis band or release delay holds detection.
    /// The engine remains the authority for the active detection state.
    /// </summary>
    private void UpdateMicrophoneThresholdHint()
    {
        if (!_microphoneMeterRunning)
        {
            MicrophoneLevelHint.Text = "Press Enable to see your microphone level.";
            return;
        }

        var triggerAmplitude = _settings.GetThresholdAmplitude();
        var releaseRatio = Math.Pow(10, -_settings.HysteresisDecibels / 20.0);
        var releaseAmplitude = triggerAmplitude * releaseRatio;

        if (_microphoneMeterRms >= triggerAmplitude)
        {
            MicrophoneLevelHint.Text = "Above threshold — sound detected";
        }
        else if (!_microphoneDetectionActive)
        {
            MicrophoneLevelHint.Text = "Below threshold";
        }
        else if (_microphoneMeterRms >= releaseAmplitude)
        {
            MicrophoneLevelHint.Text = "Detection held — hysteresis";
        }
        else
        {
            MicrophoneLevelHint.Text = "Waiting before fade back";
        }
    }

    private void ThresholdReset_Click(object sender, RoutedEventArgs args) =>
        ThresholdSlider.Value = DuckingSettings.DefaultThresholdDecibels;

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

        SetMicrophoneOperationPending(true);
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
            ApplyAndSaveSettings();
            MicrophoneNameText.Text = $"Microphone: {choice.Name}";
            if (restart) _engine.Start();
        }
        catch (Exception exception)
        {
            RefreshMicrophoneChoices();
            FeedbackText.Text = $"Microphone could not be changed: {exception.Message}";
        }
        finally
        {
            SetMicrophoneOperationPending(false);
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
}
