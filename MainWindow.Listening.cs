// VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceDucker.Audio;
using VoiceDucker.Model;

namespace VoiceDucker;

public sealed partial class MainWindow
{
    private void ConfigureListeningControls()
    {
        ProfileNameBox.MaxLength = ListeningProfile.MaxNameLength;

        var pauseMenu = new MenuFlyout();
        foreach (var minutes in VoiceDuckerModel.PauseDurations)
        {
            var item = new MenuFlyoutItem { Text = $"Pause for {minutes} minutes" };
            item.Click += async (_, _) => await _model.PauseAsync(minutes);
            pauseMenu.Items.Add(item);
        }

        PauseButton.Flyout = pauseMenu;
    }

    /// <summary>Renders model-owned profiles and pause state. Only unfinished text stays in the view.</summary>
    private void RenderListeningControls(ApplicationState state)
    {
        var choices = state.ProfileChoices;
        foreach (var combo in new[] { ProfileComboBox, SettingsProfileComboBox })
        {
            if (combo.ItemsSource is not IEnumerable<ProfileChoice> previous || !previous.SequenceEqual(choices))
                combo.ItemsSource = choices;
            var selected = choices.First(choice => choice.Id == state.Settings.SelectedProfileId);
            if (!Equals(combo.SelectedItem, selected)) combo.SelectedItem = selected;
            combo.IsEnabled = !state.Busy;
        }

        ProfileHintText.Text = state.ProfileHint;
        SettingsProfileHintText.Text = state.ProfileHint;
        UpdateProfileButton.IsEnabled = RenameProfileButton.IsEnabled = DeleteProfileButton.IsEnabled = state.CanManageProfile;
        SmoothTransitionsCheckBox.IsChecked = state.Settings.SmoothTransitions;
        PauseButton.Visibility = state.IsPaused ? Visibility.Collapsed : Visibility.Visible;
        PauseButton.IsEnabled = state.CanPause;
        PausedPanel.Visibility = state.IsPaused ? Visibility.Visible : Visibility.Collapsed;
        PauseCountdownText.Text = state.PauseText;
        ResumeButton.IsEnabled = state.CanResume;
    }

    private void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_rendering && ((ComboBox)sender).SelectedItem is ProfileChoice choice)
            _model.ApplyProfile(choice.Id);
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs args) => _model.SaveNewProfile(ProfileNameBox.Text);
    private void UpdateProfile_Click(object sender, RoutedEventArgs args) => _model.UpdateSelectedProfile();
    private void RenameProfile_Click(object sender, RoutedEventArgs args) => _model.RenameSelectedProfile(ProfileNameBox.Text);
    private void DeleteProfile_Click(object sender, RoutedEventArgs args)
    {
        _model.DeleteSelectedProfile();
        DeleteProfileButton.Flyout.Hide();
    }

    private void SmoothTransitions_Click(object sender, RoutedEventArgs args)
    {
        if (!_rendering) _model.SetSmoothTransitions(SmoothTransitionsCheckBox.IsChecked == true);
    }

    private async void Resume_Click(object sender, RoutedEventArgs args) => await _model.ResumePauseAsync();
}
