// VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using VoiceDucker.Controls;
using VoiceDucker.Model;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;

namespace VoiceDucker;

// Playback-row presentation. Levels and eligibility come from the application model.
public sealed partial class MainWindow
{
    private void RenderPlaybackRows(IReadOnlyList<PlaybackRowState> streams)
    {
        var present = streams.Select(stream => stream.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in _streamRows.Keys.Where(key => !present.Contains(key)).ToArray())
        {
            StreamRows.Children.Remove(_streamRows[key].Card);
            _streamRows.Remove(key);
        }

        foreach (var stream in streams)
        {
            if (!_streamRows.TryGetValue(stream.Key, out var row))
            {
                row = CreateStreamRow(stream);
                _streamRows.Add(stream.Key, row);
                StreamRows.Children.Add(row.Card);
                _ = LoadStreamIconAsync(stream.ProcessId, stream.Key, row.Icon);
            }
            row.Meter.Value = stream.SignalPercent;
            row.Slider.IsEnabled = stream.CanEdit;
            row.Percentage.Text = stream.VolumeText;
            ToolTipService.SetToolTip(row.Slider, stream.VolumeHelp);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(row.Slider, stream.VolumeHelp);
            if (!row.Interacting) row.Slider.Value = stream.SliderPercent;
        }
        NoStreamsText.Visibility = streams.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task LoadStreamIconAsync(int processId, string key, Image icon)
    {
        try
        {
            var png = await Task.Run(() => Audio.ProcessIcon.ReadPng(processId));
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

    private StreamRow CreateStreamRow(PlaybackRowState stream)
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
            Text = stream.VolumeText,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var title = new Grid { ColumnSpacing = 8 };
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var hide = CreateAppActionButton("Hide", stream.Name);
        hide.Opacity = 0;
        hide.Click += (_, _) => SetProcessHidden(stream.Name, true);
        var nameAndHide = new Grid { ColumnSpacing = 4, HorizontalAlignment = HorizontalAlignment.Left };
        nameAndHide.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameAndHide.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(hide, 1);
        nameAndHide.Children.Add(name);
        nameAndHide.Children.Add(hide);
        Grid.SetColumn(nameAndHide, 1);
        Grid.SetColumn(percentage, 2);
        title.Children.Add(nameAndHide);
        title.Children.Add(percentage);
        // Reserve the X's space so hovering never shifts the title or volume.
        // Keyboard focus also reveals it, without requiring a mouse.
        var pointerOverTitle = false;
        title.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        title.PointerEntered += (_, _) =>
        {
            pointerOverTitle = true;
            hide.Opacity = 1;
        };
        title.PointerExited += (_, _) =>
        {
            pointerOverTitle = false;
            hide.Opacity = hide.FocusState == FocusState.Unfocused ? 0 : 1;
        };
        hide.GotFocus += (_, _) => hide.Opacity = 1;
        hide.LostFocus += (_, _) => hide.Opacity = pointerOverTitle ? 1 : 0;
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

        var slider = new ScrollSafeSlider
        {
            Style = (Style)SettingsSplitView.Resources["VoiceDuckerSliderStyle"],
            Minimum = 0,
            Maximum = 100,
            StepFrequency = 1,
            Value = stream.SliderPercent,
            IsEnabled = stream.CanEdit
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider, $"{stream.Name} playback volume");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(slider, stream.VolumeHelp);
        ToolTipService.SetToolTip(slider, stream.VolumeHelp);
        // Keep the standard round thumb while removing the template's unused
        // vertical space, so the separate signal meter sits directly below it.
        slider.Resources["SliderPreContentMargin"] = 9d;
        slider.Resources["SliderPostContentMargin"] = 9d;
        slider.Resources["SliderHorizontalHeight"] = 22d;
        var meter = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0),
            IsHitTestVisible = false,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 246, 162, 76)),
            Style = (Style)RootPanel.Resources["PlaybackLevelMeterStyle"]
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(meter, $"{stream.Name} session output level");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(meter,
            "Session signal after the current session volume is applied, on a decibel scale. System master volume and speaker processing are not included.");
        var volumeTrack = new StackPanel { Spacing = 2 };
        ToolTipService.SetToolTip(volumeTrack, "The orange meter follows the session output after volume adjustment, on a decibel scale.");
        volumeTrack.Children.Add(slider);
        volumeTrack.Children.Add(meter);
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(title);
        panel.Children.Add(volumeTrack);
        var row = new StreamRow(panel, slider, meter, percentage, icon);
        // Slider/Thumb can handle these routed events before they reach this row.
        // Observe handled events too, so a completed drag cannot freeze mixer updates.
        slider.AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler((_, _) => row.PointerInteracting = true), true);
        slider.AddHandler(UIElement.PointerReleasedEvent,
            new PointerEventHandler((_, _) => row.PointerInteracting = false), true);
        slider.AddHandler(UIElement.PointerCanceledEvent,
            new PointerEventHandler((_, _) => row.PointerInteracting = false), true);
        slider.AddHandler(UIElement.PointerCaptureLostEvent,
            new PointerEventHandler((_, _) => row.PointerInteracting = false), true);
        // Focus can remain on the slider indefinitely; only active editing pauses refresh.
        slider.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, args) =>
        {
            if (IsVolumeSliderKey(args.Key)) row.KeyboardInteracting = true;
        }), true);
        slider.AddHandler(UIElement.KeyUpEvent, new KeyEventHandler((_, args) =>
        {
            if (IsVolumeSliderKey(args.Key)) row.KeyboardInteracting = false;
        }), true);
        slider.LostFocus += (_, _) => row.KeyboardInteracting = false;
        slider.ValueChanged += async (_, args) =>
        {
            if (_rendering)
            {
                return;
            }

            try
            {
                await _model.SetPlaybackVolumeAsync(stream.Key, args.NewValue);
            }
            catch (Exception exception)
            {
                _model.ReportViewError($"Volume could not be changed: {exception.Message}");
            }
        };
        return row;
    }

    private void SetProcessHidden(string processName, bool hidden)
    {
        _model.SetProcessHidden(processName, hidden);
        RefreshHiddenApps(_model.Current.HiddenProcesses);
    }

    /// <summary>
    /// Synchronizes hidden entries without rebuilding or dismissing the open flyout.
    /// Existing row controls are retained so keyboard focus and scroll position survive.
    /// </summary>
    private void RefreshHiddenApps(IReadOnlyList<string> names)
    {
        // Update the existing list in place: restoring an app must not dismiss the panel.
        foreach (var item in HiddenAppsList.Items.Cast<ListViewItem>()
                     .Where(item => !names.Contains((string)item.Tag, StringComparer.OrdinalIgnoreCase)).ToArray())
        {
            HiddenAppsList.Items.Remove(item);
        }
        for (var index = 0; index < names.Count; index++)
        {
            var name = names[index];
            var existingItem = index < HiddenAppsList.Items.Count
                ? (ListViewItem)HiddenAppsList.Items[index]
                : null;
            var existingName = existingItem?.Tag as string;

            if (name.Equals(existingName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var row = new Grid { ColumnSpacing = 8, Padding = new Thickness(8, 4, 4, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new TextBlock
            {
                Text = name,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTipService.SetToolTip(label, name);
            row.Children.Add(label);
            var restore = CreateAppActionButton("Show", name);
            Grid.SetColumn(restore, 1);
            row.Children.Add(restore);
            var item = new ListViewItem { Tag = name, Content = row };
            restore.Click += (_, _) =>
            {
                var restoreIndex = HiddenAppsList.Items.IndexOf(item);
                SetProcessHidden(name, false);
                if (HiddenAppsList.Items.Count > 0)
                {
                    var nextIndex = Math.Clamp(restoreIndex, 0, HiddenAppsList.Items.Count - 1);
                    var nextItem = (ListViewItem)HiddenAppsList.Items[nextIndex];
                    var nextContent = (Grid)nextItem.Content;
                    var nextShowButton = nextContent.Children.OfType<Button>().Single();

                    nextShowButton.Focus(FocusState.Keyboard);
                }
            };
            HiddenAppsList.Items.Insert(index, item);
        }
        NoHiddenAppsText.Visibility = names.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HiddenAppsList.Visibility = names.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static Button CreateAppActionButton(string action, string processName)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = "\uE711", FontSize = 12 },
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(button, action);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"{action} {processName}");
        return button;
    }

    private static bool IsVolumeSliderKey(VirtualKey key) => key is
        VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down or
        VirtualKey.Home or VirtualKey.End or VirtualKey.PageUp or VirtualKey.PageDown;

    private sealed class StreamRow(StackPanel card, Slider slider, ProgressBar meter,
        TextBlock percentage, Image icon)
    {
        public StackPanel Card { get; } = card;
        public Slider Slider { get; } = slider;
        public ProgressBar Meter { get; } = meter;
        public TextBlock Percentage { get; } = percentage;
        public Image Icon { get; } = icon;
        public bool PointerInteracting { get; set; }
        public bool KeyboardInteracting { get; set; }
        public bool Interacting => PointerInteracting || KeyboardInteracting;
    }
}
