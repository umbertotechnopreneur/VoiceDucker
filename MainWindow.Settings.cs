// VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.

using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VoiceDucker.Audio;
using VoiceDucker.Controls;
using VoiceDucker.Model;

namespace VoiceDucker;

/// <summary>Renders settings and translates control edits into model commands.</summary>
public sealed partial class MainWindow
{
    #region Implementation

    /// <summary>Renders validated model settings while preserving unfinished edits and selection.</summary>
    private void RenderSettings(ApplicationState state)
    {
        var settings = state.Settings;

        RenderNumber(HysteresisBox, settings.HysteresisDecibels);
        RenderNumber(ReductionBox, settings.ReductionPercent);
        RenderNumber(FadeDownBox, settings.FadeDownMilliseconds);
        RenderNumber(RestoreDelayBox, settings.RestoreDelayMilliseconds);
        RenderNumber(FadeUpBox, settings.FadeUpMilliseconds);
        OtherSourcesCheckBox.IsChecked = settings.IncludeOtherSources;
        StartupCheckBox.IsChecked = state.WindowsStartupEnabled;
        StartupCheckBox.IsEnabled = !state.WindowsStartupBusy;
        StartupMicrophoneCheckBox.IsChecked = settings.EnableMicrophoneOnWindowsStartup;
        StartupMicrophoneCheckBox.IsEnabled = _viewReady;
        SensitivitySlider.ApplyModelValue(settings.MicrophoneGainDecibels);
        ThresholdSlider.ApplyModelValue(settings.ThresholdDecibels);
        SensitivityValueText.Text = state.SensitivityText;
        ThresholdValueText.Text = state.ThresholdText;
        MicrophoneThresholdText.Text = $"Threshold {state.ThresholdText}";
        ReleaseThresholdText.Text = state.ReleaseThresholdText;
        ThresholdBeforeColumn.Width = new GridLength(state.ThresholdMeterFraction, GridUnitType.Star);
        ThresholdAfterColumn.Width = new GridLength(1 - state.ThresholdMeterFraction, GridUnitType.Star);

        var existingChoices = MicrophoneComboBox.ItemsSource as IEnumerable<MicrophoneChoice>;

        if (existingChoices is null || !existingChoices.SequenceEqual(state.Microphones))
        {
            MicrophoneComboBox.ItemsSource = state.Microphones;
        }

        var selected = state.Microphones.FirstOrDefault(choice => choice.Id == settings.MicrophoneDeviceId);

        if (!Equals(MicrophoneComboBox.SelectedItem, selected))
        {
            MicrophoneComboBox.SelectedItem = selected;
        }

        var section = settings.LastSettingsSection switch
        {
            "microphone" => DetectionExpander,
            "volume" => VolumeExpander,
            "apps" => AppsExpander,
            "profiles" => ProfilesExpander,
            _ => null
        };

        if (section is not null && _openSettingsExpander != section)
        {
            if (_openSettingsExpander is not null)
            {
                _openSettingsExpander.IsExpanded = false;
            }

            _openSettingsExpander = section;
            section.IsExpanded = true;
        }
    }

    /// <summary>Updates an idle numeric control without replacing a focused text buffer.</summary>
    private void RenderNumber(NumberBox control, double value)
    {
        // Keep an unfinished text edit and its caret intact while audio state updates.
        if (!ContainsFocus(control) && control.Value != value)
        {
            control.Value = value;
        }
    }

    /// <summary>Checks whether the control or any templated descendant owns keyboard focus.</summary>
    private bool ContainsFocus(DependencyObject control)
    {
        if (RootPanel.XamlRoot is null)
        {
            return false;
        }

        var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(RootPanel.XamlRoot) as DependencyObject;

        while (focused is not null)
        {
            if (focused == control)
            {
                return true;
            }

            focused = VisualTreeHelper.GetParent(focused);
        }

        return false;
    }

    /// <summary>Maps a named numeric control to its typed application-model command key.</summary>
    private static SettingKey SettingFor(NumberBox control) => control.Name switch
    {
        "HysteresisBox" => SettingKey.HysteresisDecibels,
        "ReductionBox" => SettingKey.ReductionPercent,
        "FadeDownBox" => SettingKey.FadeDownMilliseconds,
        "RestoreDelayBox" => SettingKey.RestoreDelayMilliseconds,
        "FadeUpBox" => SettingKey.FadeUpMilliseconds,
        _ => throw new InvalidOperationException("An unknown setting control was used.")
    };

    /// <summary>Forwards committed numeric values, leaving validation and persistence to the model.</summary>
    private void SettingBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_rendering)
        {
            _model.SetNumericSetting(SettingFor(sender), args.NewValue);
        }
    }

    /// <summary>Connects the NumberBox's templated text editor and focus notification once loaded.</summary>
    private void SettingBox_Loaded(object sender, RoutedEventArgs args)
    {
        var numberBox = (NumberBox)sender;
        numberBox.ApplyTemplate();
        AttachTextChanged(numberBox, numberBox);
        numberBox.LostFocus -= SettingBox_LostFocus;
        numberBox.LostFocus += SettingBox_LostFocus;
    }

    /// <summary>Restores the latest accepted model value after the user leaves a numeric editor.</summary>
    private void SettingBox_LostFocus(object sender, RoutedEventArgs args)
    {
        if (!_rendering && !_viewClosed)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_viewClosed)
                {
                    RenderState(_model.Current);
                }
            });
        }
    }

    /// <summary>Connects slider edit completion without accumulating handlers after reloads.</summary>
    private void SettingSlider_Loaded(object sender, RoutedEventArgs args)
    {
        var slider = (ScrollSafeSlider)sender;
        slider.EditingCompleted -= SettingSlider_EditingCompleted;
        slider.EditingCompleted += SettingSlider_EditingCompleted;
    }

    /// <summary>Resumes model-driven slider presentation when pointer or keyboard editing ends.</summary>
    private void SettingSlider_EditingCompleted(object? sender, EventArgs args)
    {
        if (!_viewClosed)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_viewClosed)
                {
                    RenderState(_model.Current);
                }
            });
        }
    }

    /// <summary>Finds the NumberBox's inner TextBox so valid in-progress edits reach the model.</summary>
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

    /// <summary>Parses the local text representation and delegates all numeric acceptance rules.</summary>
    private void SettingText_TextChanged(object sender, TextChangedEventArgs args)
    {
        if (_rendering || sender is not TextBox input || input.Tag is not NumberBox numberBox)
        {
            return;
        }

        var parsed = double.TryParse(input.Text, NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.CurrentCulture, out var value);
        _model.SetNumericSetting(SettingFor(numberBox), parsed ? value : double.NaN);
    }

    /// <summary>Sends the user's choice of playback sources to the model.</summary>
    private void OtherSourcesCheckBox_Click(object sender, RoutedEventArgs args)
    {
        if (!_rendering)
        {
            _model.SetIncludeOtherSources(OtherSourcesCheckBox.IsChecked == true);
        }
    }

    /// <summary>Forwards sensitivity changes without applying microphone gain in the view.</summary>
    private void SensitivitySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs args)
    {
        if (!_rendering)
        {
            _model.SetNumericSetting(SettingKey.SensitivityDecibels, args.NewValue);
        }
    }

    /// <summary>Asks the model to restore its sensitivity default.</summary>
    private void SensitivityReset_Click(object sender, RoutedEventArgs args) =>
        _model.ResetSetting(SettingKey.SensitivityDecibels);

    /// <summary>Forwards the detection threshold selected by the user.</summary>
    private void ThresholdSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs args)
    {
        if (!_rendering)
        {
            _model.SetNumericSetting(SettingKey.ThresholdDecibels, args.NewValue);
        }
    }

    /// <summary>Asks the model to restore its detection-threshold default.</summary>
    private void ThresholdReset_Click(object sender, RoutedEventArgs args) =>
        _model.ResetSetting(SettingKey.ThresholdDecibels);

    /// <summary>Refreshes microphone choices through the model before showing the list.</summary>
    private async void MicrophoneComboBox_DropDownOpened(object sender, object args) =>
        await _model.RefreshMicrophonesAsync();

    /// <summary>Forwards device selection while ignoring changes caused by rendering the list.</summary>
    private async void MicrophoneComboBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_rendering && MicrophoneComboBox.SelectedItem is MicrophoneChoice choice)
        {
            await _model.SelectMicrophoneAsync(choice.Id);
        }
    }

    /// <summary>Requests Windows autostart without changing the independent microphone opt-in.</summary>
    private async void StartupCheckBox_Click(object sender, RoutedEventArgs args)
    {
        if (!_rendering)
        {
            await _model.SetWindowsStartupAsync(StartupCheckBox.IsChecked == true);
        }
    }

    /// <summary>Saves the next-launch microphone opt-in without toggling capture immediately.</summary>
    private void StartupMicrophoneCheckBox_Click(object sender, RoutedEventArgs args)
    {
        if (!_rendering)
        {
            _model.SetMicrophoneOnStartup(StartupMicrophoneCheckBox.IsChecked == true);
        }
    }

    #endregion
}
