// VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.

using VoiceDucker.Audio;
using VoiceDucker.Licensing;

namespace VoiceDucker.Model;

/// <summary>Keeps a playback row's actual volume, normal reference, and output meter together.</summary>
internal sealed record PlaybackRowState(string Key, string Name, int ProcessId, bool IsSpotify,
    double NormalPercent, double ActualPercent, double SignalPercent, bool Muted,
    bool CanEdit, string VolumeText, string VolumeHelp)
{
    #region Public methods

    /// <summary>Positions the volume cursor at the session volume observed by the audio worker.</summary>
    public double SliderPercent => ActualPercent;

    #endregion
}

/// <summary>Contains the license information needed to render the window and purchase controls.</summary>
internal sealed record LicenseViewState(bool IsStoreEdition, bool CanUse, string WindowTitle,
    bool FooterVisible, string FooterText, bool PanelVisible, string StatusText);

/// <summary>The one application snapshot consumed by the view; audio rules live outside the view.</summary>
internal sealed record ApplicationState(DuckingSettings Settings, EngineStatus Audio, bool Busy,
    bool WindowsStartupEnabled, bool WindowsStartupBusy, LicenseViewState License, string Feedback,
    bool MonitoringReminderPending = false, bool TrialExpiredNoticePending = false)
{
    private static readonly (double Minimum, double Maximum) MeterRange =
        DuckingSettings.GetNumericRange(SettingKey.ThresholdDecibels);

    #region Public methods

    public long? PauseDeadlineTick { get; init; }
    public int PauseSecondsRemaining { get; init; }
    public bool IsPaused => PauseDeadlineTick.HasValue;
    public string PauseText => $"Paused · {PauseSecondsRemaining / 60:00}:{PauseSecondsRemaining % 60:00}";
    public bool CanPause => !Busy && Audio.Running;
    public bool CanResume => !Busy && IsPaused && License.CanUse;
    public bool CanManageProfile => !Busy && Settings.Profiles.Any(profile => profile.Id == Settings.SelectedProfileId);
    public string MonitoringButtonText => IsPaused ? "Disable (cancel pause)" : Audio.Running ? "Disable" : "Enable";
    public string TrayMonitoringText => IsPaused ? MonitoringButtonText
        : Audio.Running ? "Disable microphone ducking" : "Enable microphone ducking";
    public ListeningProfile? SelectedProfile => Settings.Profiles.FirstOrDefault(profile => profile.Id == Settings.SelectedProfileId)
        ?? ListeningPresets.Find(Settings.SelectedProfileId);
    public IReadOnlyList<ProfileChoice> ProfileChoices => new[] { new ProfileChoice(string.Empty, "Custom settings") }
        .Concat(ListeningPresets.All.Select(profile => new ProfileChoice(profile.Id,
            profile.Name + (profile.Id == ListeningPresets.DefaultId ? " · default preset" : " · preset"))))
        .Concat(Settings.Profiles.Select(profile => new ProfileChoice(profile.Id, profile.Name))).ToArray();
    public string ProfileHint
    {
        get
        {
            if (SelectedProfile is not { } profile)
            {
                return "Current settings · save a profile in Settings.";
            }

            var matches = profile.Preferences.Matches(Settings);
            if (ListeningPresets.Find(profile.Id) is not null)
            {
                return matches ? "Preset applied · use Save as new to customize."
                    : "Modified preset · use Save as new to keep these changes.";
            }

            return matches ? "Saved profile applied."
                : "Modified · choose Update saved to keep these changes.";
        }
    }

    /// <summary>Allows monitoring changes when no competing model operation is pending.</summary>
    public bool CanToggle => !Busy && (IsPaused || Audio.Running || License.CanUse);

    /// <summary>Allows input selection after playback is ready and pending operations have finished.</summary>
    public bool CanSelectMicrophone => Audio.Ready && !Busy;

    /// <summary>Includes the saved input even when that microphone is currently disconnected.</summary>
    public IReadOnlyList<MicrophoneChoice> Microphones
    {
        get
        {
            if (Audio.Microphones.Any(choice => choice.Id == Settings.MicrophoneDeviceId))
            {
                return Audio.Microphones;
            }

            return Audio.Microphones.Append(new MicrophoneChoice(Settings.MicrophoneDeviceId,
                "Unavailable microphone — choose another input")).ToArray();
        }
    }

    /// <summary>Provides normalized process names for the hidden-app list.</summary>
    public IReadOnlyList<string> HiddenProcesses => Settings.BlacklistedProcesses
        .Select(PlaybackIdentity.NormalizeProcessName)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>Projects the latest worker observations into visible rows with Spotify first.</summary>
    public IReadOnlyList<PlaybackRowState> PlaybackRows => Audio.Streams
        .Where(stream => !Settings.IsProcessExcluded(stream.Name))
        .OrderByDescending(stream => stream.IsSpotify)
        .ThenBy(stream => stream.Name, StringComparer.OrdinalIgnoreCase)
        .Select(ToPlaybackRow)
        .ToArray();

    /// <summary>Names the main visual state using readiness, capture, and observed attenuation.</summary>
    public string VisualStateText
    {
        get
        {
            if (IsPaused) return "PAUSED";
            if (!Audio.Ready)
            {
                return Busy ? "PREPARING" : "AUDIO UNAVAILABLE";
            }

            if (!Audio.Running)
            {
                return "MIC OFF";
            }

            if (Audio.AffectedSessions > 0)
            {
                return "AUDIO LOWERED";
            }

            return Audio.Speaking ? "VOICE DETECTED" : "LISTENING";
        }
    }

    /// <summary>Explains capture and restoration status, giving reported audio errors priority.</summary>
    public string StatusText
    {
        get
        {
            if (Audio.Error is { } error)
            {
                return error;
            }

            if (IsPaused) return Audio.Restoring
                ? "Paused. Microphone off; startup volumes are being restored."
                : "Paused. Microphone off; monitoring will resume automatically.";

            if (!Audio.Ready)
            {
                return "Preparing playback sessions…";
            }

            if (!Audio.Running)
            {
                return "Off. Your microphone is not in use.";
            }

            if (Audio.AffectedSessions > 0)
            {
                return $"Lowering {Audio.AffectedSessions} playing session(s) by {Audio.ReductionPercent}%.";
            }

            if (Audio.Speaking)
            {
                return "Microphone sound detected.";
            }

            return Audio.Restoring
                ? "Playback is returning to its normal volume."
                : "Listening. Playback is at its normal volume.";
        }
    }

    /// <summary>Uses the active capture name, falling back to the selected microphone choice.</summary>
    public string MicrophoneNameText
    {
        get
        {
            var microphoneName = !string.IsNullOrEmpty(Audio.MicrophoneName)
                ? Audio.MicrophoneName
                : Microphones.FirstOrDefault(choice => choice.Id == Settings.MicrophoneDeviceId)?.Name
                    ?? "Unavailable";

            return $"Microphone: {microphoneName}";
        }
    }

    /// <summary>Formats the gain-adjusted microphone measurement already classified by the worker.</summary>
    public string MicrophoneLevelText => !Audio.Running ? "Off" : double.IsNegativeInfinity(Audio.Detection.Decibels)
        ? "−∞ dBFS" : $"{Audio.Detection.Decibels:0.0} dBFS";

    /// <summary>Describes the worker's detection phase without classifying the microphone again.</summary>
    public string MicrophoneLevelHint => Audio.Detection.Phase switch
    {
        DetectionPhase.Off => "Press Enable to see your microphone level.",
        DetectionPhase.AboveThreshold => "Above threshold — sound detected",
        DetectionPhase.Hysteresis => "Detection held — hysteresis",
        DetectionPhase.Hold => "Waiting before fade back",
        DetectionPhase.Stale => "Microphone data is unavailable.",
        _ => "Below threshold"
    };

    /// <summary>Formats the selected software gain in decibels.</summary>
    public string SensitivityText => $"{Settings.MicrophoneGainDecibels:+0;-0;0} dB";

    /// <summary>Formats the selected detection threshold on the microphone meter scale.</summary>
    public string ThresholdText => $"{Settings.ThresholdDecibels:0.0} dBFS";

    /// <summary>Explains the selected hysteresis release level and the following release delay.</summary>
    public string ReleaseThresholdText => $"Release below {Settings.ThresholdDecibels - Settings.HysteresisDecibels:0.0} dBFS, then wait before fade back.";

    /// <summary>Maps the current microphone measurement onto the meter's 60 dB display range.</summary>
    public double MicrophoneMeterFraction => Audio.Running ? SignalPercent(Audio.Detection.Rms) / 100 : 0;

    /// <summary>Positions the configured threshold on the same 60 dB display range.</summary>
    public double ThresholdMeterFraction => MeterFraction(Settings.ThresholdDecibels);

    /// <summary>Scales the microphone animation independently of the calibrated level meter.</summary>
    public double AnimationLevel => Audio.Running ? Math.Clamp(Audio.Detection.Rms / 0.08, 0, 1) : 0;

    #endregion

    #region Implementation

    /// <summary>Derives a row's volume limits, meter, and help from one immutable playback observation.</summary>
    private PlaybackRowState ToPlaybackRow(PlaybackStream stream)
    {
        var lowering = Audio.Running && Audio.Speaking && stream.IsEligible && Audio.ReductionPercent > 0;
        var maximum = lowering ? 100 - Audio.ReductionPercent : 100;
        var help = stream switch
        {
            { Muted: true } => "This session is muted. Unmute it in the playing app or Windows mixer to adjust playback.",
            { HasAudibleSignal: false } => "No playback signal detected for this session. Its meter stays empty and no new automatic reduction is applied. Resume playback in this app to adjust or automatically lower it.",
            _ when maximum == 0 => "Volume is held at zero while reduction is 100%. Lower Reduce volume by to adjust playback.",
            _ when lowering => $"Current playback volume. Maximum {maximum}% while lowering. Adjusting it also updates the normal level used when sound detection ends.",
            _ => "Current playback volume. Adjusting it sets the normal level used when sound detection ends."
        };
        if (!stream.IsEligible)
            help += " Automatic lowering is off for this app. Turn on Also lower other apps in Apps & startup to include it.";

        var normalPercent = stream.NormalVolume * 100;
        var actualPercent = stream.ActualVolume * 100;
        var outputAmplitude = stream.Muted ? 0 : stream.Peak * stream.ActualVolume;
        var canEdit = !stream.Muted && stream.HasAudibleSignal && Audio.Ready && maximum > 0;
        var volumeText = stream.Muted
            ? "MUTED"
            : $"Volume {actualPercent:0}% · Normal {normalPercent:0}%" +
                (stream.HasAudibleSignal ? string.Empty : " · No signal");

        return new PlaybackRowState(
            stream.Key,
            stream.Name,
            stream.ProcessId,
            stream.IsSpotify,
            normalPercent,
            actualPercent,
            SignalPercent(outputAmplitude),
            stream.Muted,
            canEdit,
            volumeText,
            help);
    }

    /// <summary>Maps a finite amplitude onto the shared logarithmic range from -60 to 0 dBFS.</summary>
    private static double SignalPercent(double amplitude)
    {
        if (amplitude <= 0 || !double.IsFinite(amplitude))
        {
            return 0;
        }

        return MeterFraction(20 * Math.Log10(amplitude)) * 100;
    }

    private static double MeterFraction(double decibels) =>
        Math.Clamp((decibels - MeterRange.Minimum) / (MeterRange.Maximum - MeterRange.Minimum), 0, 1);

    #endregion
}

internal sealed record ProfileChoice(string Id, string Name);
