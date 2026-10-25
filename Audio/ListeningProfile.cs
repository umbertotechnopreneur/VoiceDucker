// VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.

namespace VoiceDucker.Audio;

/// <summary>A named, manually applied set of audio preferences, independent of capture and startup.</summary>
internal sealed record ListeningProfile(string Id, string Name, ListeningPreferences Preferences)
{
    public bool IsValid => Guid.TryParse(Id, out _) && !string.IsNullOrWhiteSpace(Name) &&
        Name == Name.Trim() && Name.Length <= 40 && !Name.Any(char.IsControl) &&
        Preferences is not null && Preferences.IsValid;
}

/// <summary>Only listening choices belong to a profile; device, runtime volumes and startup do not.</summary>
internal sealed record ListeningPreferences(int ReductionPercent, int FadeDownMilliseconds,
    int FadeUpMilliseconds, int RestoreDelayMilliseconds, double ThresholdDecibels,
    int HysteresisDecibels, int MicrophoneGainDecibels, bool IncludeOtherSources,
    bool SmoothTransitions, string[] BlacklistedProcesses)
{
    public static ListeningPreferences Capture(DuckingSettings settings) => new(
        settings.ReductionPercent, settings.FadeDownMilliseconds, settings.FadeUpMilliseconds,
        settings.RestoreDelayMilliseconds, settings.ThresholdDecibels, settings.HysteresisDecibels,
        settings.MicrophoneGainDecibels, settings.IncludeOtherSources, settings.SmoothTransitions,
        settings.BlacklistedProcesses.ToArray());

    public DuckingSettings ApplyTo(DuckingSettings settings) => settings with
    {
        ReductionPercent = ReductionPercent,
        FadeDownMilliseconds = FadeDownMilliseconds,
        FadeUpMilliseconds = FadeUpMilliseconds,
        RestoreDelayMilliseconds = RestoreDelayMilliseconds,
        ThresholdDecibels = ThresholdDecibels,
        HysteresisDecibels = HysteresisDecibels,
        MicrophoneGainDecibels = MicrophoneGainDecibels,
        IncludeOtherSources = IncludeOtherSources,
        SmoothTransitions = SmoothTransitions,
        BlacklistedProcesses = BlacklistedProcesses.ToArray()
    };

    public bool Matches(DuckingSettings settings)
    {
        var current = Capture(settings);
        return (this with { BlacklistedProcesses = current.BlacklistedProcesses }) == current &&
            BlacklistedProcesses.SequenceEqual(current.BlacklistedProcesses, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsValid => BlacklistedProcesses is not null &&
        ApplyTo(new DuckingSettings()).IsValid;
}
