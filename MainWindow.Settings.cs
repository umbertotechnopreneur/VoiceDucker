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
            MicrophoneComboBox.ItemsSource = state.Microphones;
        var selected = state.Microphones.FirstOrDefault(choice => choice.Id == settings.MicrophoneDeviceId);
        if (!Equals(MicrophoneComboBox.SelectedItem, selected))
            MicrophoneComboBox.SelectedItem = selected;

        var section = settings.LastSettingsSection switch
        {
            "microphone" => DetectionExpander,
            "volume" => VolumeExpander,
            "apps" => AppsExpander,
            _ => null
        };
        if (section is not null && _openSettingsExpander != section)
        {
            if (_openSettingsExpander is not null) _openSettingsExpander.IsExpanded = false;
            _openSettingsExpander = section;
            section.IsExpanded = true;
        }
    }

    private void RenderNumber(NumberBox control, double value)
    {
        // Keep an unfinished text edit and its caret intact while audio state updates.
        if (!ContainsFocus(control) && control.Value != value) control.Value = value;
    }

    private bool ContainsFocus(DependencyObject control)
    {
        if (RootPanel.XamlRoot is null) return false;
        var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(RootPanel.XamlRoot) as DependencyObject;
        while (focused is not null)
        {
            if (focused == control) return true;
            focused = VisualTreeHelper.GetParent(focused);
        }
        return false;
    }

    private static SettingKey SettingFor(NumberBox control) => control.Name switch
    {
        "HysteresisBox" => SettingKey.HysteresisDecibels,
        "ReductionBox" => SettingKey.ReductionPercent,
        "FadeDownBox" => SettingKey.FadeDownMilliseconds,
        "RestoreDelayBox" => SettingKey.RestoreDelayMilliseconds,
        "FadeUpBox" => SettingKey.FadeUpMilliseconds,
        _ => throw new InvalidOperationException("An unknown setting control was used.")
    };

    private void SettingBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_rendering) _model.SetNumericSetting(SettingFor(sender), args.NewValue);
    }

    private void SettingBox_Loaded(object sender, RoutedEventArgs args)
    {
        var numberBox = (NumberBox)sender;
        numberBox.ApplyTemplate();
        AttachTextChanged(numberBox, numberBox);
        numberBox.LostFocus -= SettingBox_LostFocus;
        numberBox.LostFocus += SettingBox_LostFocus;
    }

    private void SettingBox_LostFocus(object sender, RoutedEventArgs args)
    {
        if (!_rendering && !_viewClosed)
            DispatcherQueue.TryEnqueue(() => { if (!_viewClosed) RenderState(_model.Current); });
    }

    private void SettingSlider_Loaded(object sender, RoutedEventArgs args)
    {
        var slider = (ScrollSafeSlider)sender;
        slider.EditingCompleted -= SettingSlider_EditingCompleted;
        slider.EditingCompleted += SettingSlider_EditingCompleted;
    }

    private void SettingSlider_EditingCompleted(object? sender, EventArgs args)
    {
        if (!_viewClosed)
            DispatcherQueue.TryEnqueue(() => { if (!_viewClosed) RenderState(_model.Current); });
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
            AttachTextChanged(numberBox, VisualTreeHelper.GetChild(element, index));
    }

    private void SettingText_TextChanged(object sender, TextChangedEventArgs args)
    {
        if (_rendering || sender is not TextBox input || input.Tag is not NumberBox numberBox) return;
        var parsed = double.TryParse(input.Text, NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.CurrentCulture, out var value);
        _model.SetNumericSetting(SettingFor(numberBox), parsed ? value : double.NaN);
    }

    private void OtherSourcesCheckBox_Click(object sender, RoutedEventArgs args)
    {
        if (!_rendering) _model.SetIncludeOtherSources(OtherSourcesCheckBox.IsChecked == true);
    }

    private void SensitivitySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs args)
    {
        if (!_rendering) _model.SetNumericSetting(SettingKey.SensitivityDecibels, args.NewValue);
    }

    private void SensitivityReset_Click(object sender, RoutedEventArgs args) =>
        _model.ResetSetting(SettingKey.SensitivityDecibels);

    private void ThresholdSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs args)
    {
        if (!_rendering) _model.SetNumericSetting(SettingKey.ThresholdDecibels, args.NewValue);
    }

    private void ThresholdReset_Click(object sender, RoutedEventArgs args) =>
        _model.ResetSetting(SettingKey.ThresholdDecibels);

    private async void MicrophoneComboBox_DropDownOpened(object sender, object args) =>
        await _model.RefreshMicrophonesAsync();

    private async void MicrophoneComboBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_rendering && MicrophoneComboBox.SelectedItem is MicrophoneChoice choice)
            await _model.SelectMicrophoneAsync(choice.Id);
    }

    private async void StartupCheckBox_Click(object sender, RoutedEventArgs args)
    {
        if (!_rendering) await _model.SetWindowsStartupAsync(StartupCheckBox.IsChecked == true);
    }

    private void StartupMicrophoneCheckBox_Click(object sender, RoutedEventArgs args)
    {
        if (!_rendering) _model.SetMicrophoneOnStartup(StartupMicrophoneCheckBox.IsChecked == true);
    }
}
