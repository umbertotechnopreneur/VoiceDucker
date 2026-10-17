// VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using VoiceDucker.Audio;
using Windows.Storage.Streams;
using Windows.UI;

namespace VoiceDucker;

// Playback rows and hidden-app presentation. Audio volume ownership stays in the engine.
public sealed partial class MainWindow
{
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
        if (layoutChanged && !_windowPlacement.IsTracking && !_restoredWindowPlacement)
        {
            FitMainWindow();
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

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            StepFrequency = 1,
            Value = Math.Clamp(stream.Volume * 100, 0, 100),
            IsEnabled = !stream.Muted
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider, $"{stream.Name} volume");
        // The meter draws the track; the native slider keeps its thumb above it in every state.
        foreach (var state in new[] { "", "PointerOver", "Pressed", "Disabled" })
        {
            slider.Resources[$"SliderTrackFill{state}"] = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            slider.Resources[$"SliderTrackValueFill{state}"] = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        }
        var meter = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0),
            IsHitTestVisible = false,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 246, 162, 76)),
            Style = (Style)RootPanel.Resources["PlaybackLevelMeterStyle"]
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(meter, $"{stream.Name} audio level");
        var volumeTrack = new Grid();
        Canvas.SetZIndex(slider, 1);
        volumeTrack.Children.Add(meter);
        volumeTrack.Children.Add(slider);
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
        ApplyAndSaveSettings();
        RefreshHiddenApps();
        RefreshStreamRows();
    }

    /// <summary>
    /// Synchronizes hidden entries without rebuilding or dismissing the open flyout.
    /// Existing row controls are retained so keyboard focus and scroll position survive.
    /// </summary>
    private void RefreshHiddenApps()
    {
        var names = _settings.BlacklistedProcesses.Select(PlaybackIdentity.NormalizeProcessName)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();

        // Update the existing list in place: restoring an app must not dismiss the panel.
        foreach (var item in HiddenAppsList.Items.Cast<ListViewItem>()
                     .Where(item => !names.Contains((string)item.Tag, StringComparer.OrdinalIgnoreCase)).ToArray())
        {
            HiddenAppsList.Items.Remove(item);
        }
        for (var index = 0; index < names.Length; index++)
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
        NoHiddenAppsText.Visibility = names.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        HiddenAppsList.Visibility = names.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
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
}
