/* VBWR B
 * Project: VoiceDucker
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Modified with AI: OpenAI Codex; native Cassette 1984 instruments, 2026-10-10.
 * Human guidance: Cream analog L/R meters, moving needles and retro spectrum.
 * Copyright (c) 2026 Umberto Giacobbi
 * SPDX-License-Identifier: MIT
 * VBWR E */

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using VoiceDucker.Audio;
using Windows.Foundation;
using Windows.UI;

namespace VoiceDucker.Visuals;

internal sealed class CassetteVisualizer : StackPanel
{
    internal static FontFamily RetroFont { get; } =
        new("ms-appx:///Assets/Fonts/PressStart2P-Regular.ttf#Press Start 2P");
    private const int SegmentCount = 12;
    private readonly AnalogMeter _left = new("L");
    private readonly AnalogMeter _right = new("R");
    private readonly Border[,] _segments = new Border[AudioSpectrum.BandCount, SegmentCount];
    private readonly float[] _levels = new float[AudioSpectrum.BandCount];
    private readonly int[] _lit = new int[AudioSpectrum.BandCount];
    private readonly TextBlock _status;
    private long _lastTick = Environment.TickCount64;
    private string? _lastError;

    public CassetteVisualizer()
    {
        Spacing = 8;
        var meters = new Grid { ColumnSpacing = 8, Height = 132, Margin = new Thickness(0, 4, 0, 4) };
        meters.ColumnDefinitions.Add(new ColumnDefinition());
        meters.ColumnDefinitions.Add(new ColumnDefinition());
        meters.Children.Add(_left);
        Grid.SetColumn(_right, 1);
        meters.Children.Add(_right);
        Children.Add(meters);

        var spectrum = new Grid { ColumnSpacing = 3, Height = 58 };
        for (var band = 0; band < AudioSpectrum.BandCount; band++)
        {
            spectrum.ColumnDefinitions.Add(new ColumnDefinition());
            var stack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Bottom };
            for (var segment = SegmentCount - 1; segment >= 0; segment--)
            {
                var cell = new Border
                {
                    Height = 3,
                    Background = Brush(segment >= 10 ? 0xD8963Cu : segment >= 8 ? 0xD7CD51u : 0x83B94Fu),
                    Opacity = 0.12
                };
                _segments[band, segment] = cell;
                stack.Children.Add(cell);
            }
            Grid.SetColumn(stack, band);
            spectrum.Children.Add(stack);
        }
        var labels = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        for (var index = 0; index < 3; index++)
        {
            labels.ColumnDefinitions.Add(new ColumnDefinition());
            var text = Label(new[] { "BASS", "MID", "TREBLE" }[index], 8, 0xD6D3C8);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(text, index);
            labels.Children.Add(text);
        }
        var spectrumPanel = new StackPanel();
        spectrumPanel.Children.Add(spectrum);
        spectrumPanel.Children.Add(labels);
        Children.Add(new Border
        {
            Child = spectrumPanel, Padding = new Thickness(10, 5, 10, 8),
            Background = Brush(0x121413), BorderBrush = Brush(0x080909),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3)
        });
        _status = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"), FontSize = 10, Height = 14,
            Foreground = Brush(0xD6D3C8), Text = "Waiting for app audio..."
        };
        Children.Add(_status);
    }

    public void Update(AudioVisualizationFrame frame, bool muted)
    {
        var now = Environment.TickCount64;
        var seconds = Math.Clamp((now - _lastTick) / 1000.0, 0.001, 0.1);
        _lastTick = now;
        var current = frame.Ready && now - frame.Timestamp < 250 && !muted;
        _left.Update(current ? frame.LeftRms : 0, seconds);
        _right.Update(current ? frame.RightRms : 0, seconds);
        for (var band = 0; band < AudioSpectrum.BandCount; band++)
        {
            var target = current && band < frame.Bands.Length ? frame.Bands[band] : 0;
            var blend = 1 - Math.Exp(-seconds / (target > _levels[band] ? 0.04 : 0.22));
            _levels[band] += (float)(blend * (target - _levels[band]));
            var lit = (int)Math.Round(_levels[band] * SegmentCount);
            if (lit == _lit[band]) continue;
            _lit[band] = lit;
            for (var segment = 0; segment < SegmentCount; segment++)
                _segments[band, segment].Opacity = segment < lit ? 1 : 0.12;
        }
        _status.Text = frame.Error is not null ? "Visualizer unavailable for this app."
            : !frame.Ready ? "Waiting for app audio..." : muted ? "MUTED" : "";
        if (_lastError != frame.Error)
        {
            _lastError = frame.Error;
            // Keep the actual Windows failure available without obscuring mixer controls.
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(this,
                frame.Error ?? "Stereo VU: 0 = -18 dBFS RMS. Spectrum: 40 Hz to 16 kHz.");
        }
    }

    internal static SolidColorBrush Brush(uint rgb) => new(Color.FromArgb(255,
        (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));

    private static TextBlock Label(string text, double size, uint color) => new()
    {
        Text = text, FontFamily = RetroFont, FontSize = size, Foreground = Brush(color)
    };

    private sealed class AnalogMeter : Grid
    {
        private readonly RotateTransform _needleRotation;
        private double _angle = -55;
        private double _velocity;

        public AnalogMeter(string channel)
        {
            var face = new Canvas { Width = 300, Height = 195 };
            var points = new PointCollection();
            for (var tick = 0; tick <= 36; tick++)
            {
                var angle = -55 + tick * 110.0 / 36;
                var point = DialPoint(angle, 123);
                points.Add(point);
                var outer = DialPoint(angle, tick % 3 == 0 ? 140 : 133);
                face.Children.Add(new Line
                {
                    X1 = point.X, Y1 = point.Y, X2 = outer.X, Y2 = outer.Y,
                    Stroke = Brush(angle > 40 ? 0xB34B23u : 0x222723u), StrokeThickness = tick % 3 == 0 ? 3 : 1.5
                });
            }
            face.Children.Add(new Polyline { Points = points, Stroke = Brush(0x222723), StrokeThickness = 2 });
            foreach (var db in new[] { -20, -10, -5, 0, 3 })
            {
                var angle = AngleForDb(db);
                var point = DialPoint(angle, 154);
                var label = Label(db > 0 ? $"+{db}" : db.ToString(), 12, db > 0 ? 0xB34B23u : 0x222723u);
                label.Width = 46;
                label.TextAlignment = TextAlignment.Center;
                Canvas.SetLeft(label, Math.Clamp(point.X - 23, 2, 252));
                Canvas.SetTop(label, Math.Max(10, point.Y - 7));
                face.Children.Add(label);
            }
            var channelLabel = Label(channel, 22, 0x222723);
            Canvas.SetLeft(channelLabel, 20);
            Canvas.SetTop(channelLabel, 140);
            face.Children.Add(channelLabel);
            _needleRotation = new RotateTransform { CenterX = 150, CenterY = 175 };
            var needle = new Canvas { RenderTransform = _needleRotation };
            needle.Children.Add(new Line
            {
                X1 = 152, Y1 = 184, X2 = 152, Y2 = 37,
                Stroke = Brush(0x6C5138), StrokeThickness = 4, Opacity = 0.25,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
            });
            var needleFinish = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(1, 0)
            };
            needleFinish.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 130, 48, 18), Offset = 0 });
            needleFinish.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 233, 150, 84), Offset = 0.4 });
            needleFinish.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 185, 72, 28), Offset = 1 });
            needle.Children.Add(new Polygon
            {
                Points = new PointCollection
                {
                    new(147, 184), new(149, 46), new(150, 35), new(151, 46), new(153, 184)
                },
                Fill = needleFinish, Stroke = Brush(0x873C1F), StrokeThickness = 0.6
            });
            needle.Children.Add(new Line
            {
                X1 = 149.5, Y1 = 171, X2 = 149.8, Y2 = 50,
                Stroke = Brush(0xF3BE82), StrokeThickness = 0.7, Opacity = 0.65
            });
            face.Children.Add(needle);
            var cap = new Ellipse
            {
                Width = 24, Height = 24, Fill = Brush(0x121714), Stroke = Brush(0x777B70), StrokeThickness = 1.5
            };
            Canvas.SetLeft(cap, 138);
            Canvas.SetTop(cap, 163);
            face.Children.Add(cap);
            var capFinish = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            capFinish.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 111, 118, 105), Offset = 0 });
            capFinish.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 29, 36, 30), Offset = 0.55 });
            capFinish.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 12, 17, 14), Offset = 1 });
            var capInset = new Ellipse { Width = 17, Height = 17, Fill = capFinish };
            Canvas.SetLeft(capInset, 141.5);
            Canvas.SetTop(capInset, 166.5);
            face.Children.Add(capInset);
            var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            gradient.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 245, 239, 217), Offset = 0 });
            gradient.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 218, 205, 168), Offset = 1 });
            var frame = new Border
            {
                Child = face, Background = gradient, BorderBrush = Brush(0x0B0D0C),
                BorderThickness = new Thickness(5), CornerRadius = new CornerRadius(4)
            };
            Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = frame });
            PlaceNeedle();
        }

        public void Update(double rms, double seconds)
        {
            // VU-style RMS reference, independent of the application's volume slider.
            var db = 20 * Math.Log10(Math.Max(1e-7, rms)) + 18;
            var target = AngleForDb(db);
            // A damped mechanical movement: small integration steps remain stable after UI stalls.
            var steps = Math.Max(1, (int)Math.Ceiling(seconds / 0.008));
            var step = seconds / steps;
            for (var index = 0; index < steps; index++)
            {
                _velocity += (180 * (target - _angle) - 24 * _velocity) * step;
                _angle += _velocity * step;
                if (_angle < -55 || _angle > 55)
                {
                    _angle = Math.Clamp(_angle, -55, 55);
                    _velocity = 0;
                }
            }
            PlaceNeedle();
        }

        private void PlaceNeedle()
        {
            _needleRotation.Angle = _angle;
        }

        private static double AngleForDb(double db) => -55 + Math.Clamp((db + 20) / 23, 0, 1) * 110;
        private static Point DialPoint(double angle, double radius)
        {
            var radians = angle * Math.PI / 180;
            return new Point(150 + Math.Sin(radians) * radius, 175 - Math.Cos(radians) * radius);
        }
    }
}
