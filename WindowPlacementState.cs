// VoiceDucker — Copyright (c) 2026 Umberto Giacobbi. MIT license.

using System.Text.Json.Serialization;

namespace VoiceDucker;

internal sealed record WindowBounds(int X, int Y, int Width, int Height)
{
    [JsonIgnore]
    public bool IsValid => Width is > 0 and <= 100_000 && Height is > 0 and <= 100_000 &&
                           X is >= -1_000_000 and <= 1_000_000 && Y is >= -1_000_000 and <= 1_000_000;
}

internal sealed record WindowPlacementState(
    WindowBounds Bounds,
    WindowBounds WorkArea,
    uint Dpi,
    string MonitorId,
    string MonitorName,
    bool Maximized,
    bool SettingsPaneOpen)
{
    [JsonIgnore]
    public bool IsValid => Bounds is { IsValid: true } && WorkArea is { IsValid: true } &&
                           Dpi is >= 48 and <= 960 && MonitorId is not null && MonitorName is not null;

    public WindowBounds RestoreBounds(WindowBounds targetWorkArea, uint targetDpi)
    {
        // Store physical pixels, but preserve logical size and monitor-relative offsets
        // if Windows scaling or the desktop arrangement changes between launches.
        var ratio = targetDpi / (double)Dpi;
        var width = Math.Clamp((int)Math.Round(Bounds.Width * ratio), 1, targetWorkArea.Width);
        var height = Math.Clamp((int)Math.Round(Bounds.Height * ratio), 1, targetWorkArea.Height);
        var x = targetWorkArea.X + (int)Math.Round((Bounds.X - WorkArea.X) * ratio);
        var y = targetWorkArea.Y + (int)Math.Round((Bounds.Y - WorkArea.Y) * ratio);
        return new WindowBounds(
            Math.Clamp(x, targetWorkArea.X, targetWorkArea.X + targetWorkArea.Width - width),
            Math.Clamp(y, targetWorkArea.Y, targetWorkArea.Y + targetWorkArea.Height - height),
            width, height);
    }
}
