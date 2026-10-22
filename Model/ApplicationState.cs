// VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.

using VoiceDucker.Audio;
using VoiceDucker.Licensing;

namespace VoiceDucker.Model;

internal enum SettingKey
{
    ReductionPercent, FadeDownMilliseconds, FadeUpMilliseconds, RestoreDelayMilliseconds,
    HysteresisDecibels, SensitivityDecibels, ThresholdDecibels
}

internal sealed record PlaybackRowState(string Key, string Name, int ProcessId, bool IsSpotify,
    double NormalPercent, double ActualPercent, double SignalPercent, bool Muted,
    bool CanEdit, string VolumeText, string VolumeHelp)
{
    public double SliderPercent => ActualPercent;
}

internal sealed record LicenseViewState(bool IsStoreEdition, bool CanUse, string WindowTitle,
    bool FooterVisible, string FooterText, bool PanelVisible, string StatusText);

/// <summary>The one application snapshot consumed by the view; audio rules live outside the view.</summary>
internal sealed record ApplicationState(DuckingSettings Settings, EngineStatus Audio, bool Busy,
    bool WindowsStartupEnabled, bool WindowsStartupBusy, LicenseViewState License, string Feedback,
    bool MonitoringReminderPending = false, bool TrialExpiredNoticePending = false)
{
    public bool CanToggle => !Busy && (Audio.Running || License.CanUse);
    public bool CanSelectMicrophone => Audio.Ready && !Busy;
    public IReadOnlyList<MicrophoneChoice> Microphones
    {
        get
        {
            if (Audio.Microphones.Any(choice => choice.Id == Settings.MicrophoneDeviceId))
                return Audio.Microphones;
            return Audio.Microphones.Append(new MicrophoneChoice(Settings.MicrophoneDeviceId,
                "Unavailable microphone — choose another input")).ToArray();
        }
    }
    public IReadOnlyList<string> HiddenProcesses => Settings.BlacklistedProcesses
        .Select(PlaybackIdentity.NormalizeProcessName).Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase).ToArray();
    public IReadOnlyList<PlaybackRowState> PlaybackRows => Audio.Streams
        .Where(stream => !Settings.IsProcessExcluded(stream.Name))
        .OrderByDescending(stream => stream.IsSpotify).ThenBy(stream => stream.Name, StringComparer.OrdinalIgnoreCase)
        .Select(ToPlaybackRow)
        .ToArray();

    private PlaybackRowState ToPlaybackRow(PlaybackStream stream)
    {
        var lowering = Audio.Running && Audio.Speaking && stream.IsEligible;
        var maximum = lowering ? 100 - Settings.ReductionPercent : 100;
        var help = maximum == 0
            ? "Volume is held at zero while reduction is 100%. Lower Reduce volume by to adjust playback."
            : lowering
                ? $"Current playback volume. Maximum {maximum}% while lowering. Adjusting it also updates the normal level used when sound detection ends."
                : "Current playback volume. Adjusting it sets the normal level used when sound detection ends.";
        return new PlaybackRowState(stream.Key, stream.Name, stream.ProcessId, stream.IsSpotify,
            stream.NormalVolume * 100, stream.ActualVolume * 100,
            SignalPercent(stream.Muted ? 0 : stream.Peak * stream.ActualVolume), stream.Muted,
            !stream.Muted && Audio.Ready && maximum > 0,
            stream.Muted ? "MUTED" : $"Volume {stream.ActualVolume * 100:0.#}% · Normal {stream.NormalVolume * 100:0}%", help);
    }
    public string VisualStateText => !Audio.Ready ? Busy ? "PREPARING" : "AUDIO UNAVAILABLE" : !Audio.Running ? "MIC OFF" :
        Audio.AffectedSessions > 0 ? "AUDIO LOWERED" : Audio.Speaking ? "VOICE DETECTED" : "LISTENING";
    public string StatusText => Audio.Error ?? (!Audio.Ready ? "Preparing playback sessions…" :
        !Audio.Running ? "Off. Your microphone is not in use." : Audio.AffectedSessions > 0
            ? $"Lowering {Audio.AffectedSessions} playing session(s) by {Settings.ReductionPercent}%."
            : Audio.Speaking ? "Microphone sound detected." : Audio.Restoring
                ? "Playback is returning to its normal volume." : "Listening. Playback is at its normal volume.");
    public string MicrophoneNameText => $"Microphone: {(!string.IsNullOrEmpty(Audio.MicrophoneName)
        ? Audio.MicrophoneName : Microphones.FirstOrDefault(choice => choice.Id == Settings.MicrophoneDeviceId)?.Name ?? "Unavailable")}";
    public string MicrophoneLevelText => !Audio.Running ? "Off" : double.IsNegativeInfinity(Audio.Detection.Decibels)
        ? "−∞ dBFS" : $"{Audio.Detection.Decibels:0.0} dBFS";
    public string MicrophoneLevelHint => Audio.Detection.Phase switch
    {
        DetectionPhase.Off => "Press Enable to see your microphone level.",
        DetectionPhase.AboveThreshold => "Above threshold — sound detected",
        DetectionPhase.Hysteresis => "Detection held — hysteresis",
        DetectionPhase.Hold => "Waiting before fade back",
        DetectionPhase.Stale => "Microphone data is unavailable.",
        _ => "Below threshold"
    };
    public string SensitivityText => $"{Settings.MicrophoneGainDecibels:+0;-0;0} dB";
    public string ThresholdText => $"{Settings.ThresholdDecibels:0.0} dBFS";
    public string ReleaseThresholdText => $"Release below {Settings.ThresholdDecibels - Settings.HysteresisDecibels:0.0} dBFS, then wait before fade back.";
    public double MicrophoneMeterFraction => Audio.Running ? SignalPercent(Audio.Detection.Rms) / 100 : 0;
    public double ThresholdMeterFraction => Math.Clamp((Settings.ThresholdDecibels + 60) / 60, 0, 1);
    public double AnimationLevel => Audio.Running ? Math.Clamp(Audio.Detection.Rms / 0.08, 0, 1) : 0;

    private static double SignalPercent(double amplitude) => amplitude <= 0 || !double.IsFinite(amplitude)
        ? 0 : Math.Clamp((20 * Math.Log10(amplitude) + 60) / 60 * 100, 0, 100);
}
