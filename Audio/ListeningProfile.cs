// VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.

namespace VoiceDucker.Audio;

/// <summary>A named, manually applied set of audio preferences, independent of capture and startup.</summary>
internal sealed record ListeningProfile(string Id, string Name, ListeningPreferences Preferences)
{
    public bool IsValid => Guid.TryParse(Id, out _) && !string.IsNullOrWhiteSpace(Name) &&
        Name == Name.Trim() && Name.Length <= 40 && !Name.Any(char.IsControl) &&
        Preferences is not null && Preferences.IsValid;
}

/// <summary>Always-available starting points. Personal profiles are saved separately and never replaced.</summary>
internal static class ListeningPresets
{
    public const string DefaultId = "a7a61001-392d-4206-8451-a820366a0001";

    public static IReadOnlyList<ListeningProfile> All { get; } = Array.AsReadOnly(new[]
    {
        Create(DefaultId, "Talking to AI", 90, 300, 650, 2000),
        Create("a7a61001-392d-4206-8451-a820366a0002", "Client calls", 95, 150, 1500, 3000),
        Create("a7a61001-392d-4206-8451-a820366a0003", "Meetings", 80, 400, 1200, 2500),
        Create("a7a61001-392d-4206-8451-a820366a0004", "Recording a demo", 95, 150, 1800, 3000),
        Create("a7a61001-392d-4206-8451-a820366a0005", "Relaxed conversation", 50, 700, 2000, 4000)
    });

    public static ListeningProfile? Find(string id) => All.FirstOrDefault(profile => profile.Id == id);

    private static ListeningProfile Create(string id, string name, int reduction, int fadeDown, int wait, int fadeUp) =>
        new(id, name, new ListeningPreferences(reduction, fadeDown, fadeUp, wait,
            DuckingSettings.DefaultThresholdDecibels, 3, 0, false, true, []));
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
