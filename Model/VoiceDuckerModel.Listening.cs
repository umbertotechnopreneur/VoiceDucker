// VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.

using VoiceDucker.Audio;

namespace VoiceDucker.Model;

internal sealed partial class VoiceDuckerModel
{
    private int _pauseResumeQueued;

    public void SetSmoothTransitions(bool enabled) =>
        UpdateSettings(settings => settings with { SmoothTransitions = enabled });

    /// <summary>Applies all listening choices together without changing monitoring or hardware.</summary>
    public void ApplyProfile(string id)
    {
        if (id.Length == 0)
        {
            UpdateSettings(settings => settings with { SelectedProfileId = string.Empty }, applyAudio: false);
            return;
        }

        var profile = Current.Settings.Profiles.FirstOrDefault(item => item.Id == id) ?? ListeningPresets.Find(id);
        if (profile is null) { ReportViewError("This profile is no longer available."); return; }
        UpdateSettings(settings => profile.Preferences.ApplyTo(settings) with { SelectedProfileId = id });
    }

    public void SaveNewProfile(string name)
    {
        name = name.Trim();
        if (!AcceptProfileName(name)) return;
        if (Current.Settings.Profiles.Length >= 32)
        {
            ReportViewError("You can save up to 32 profiles. Delete one before adding another.");
            return;
        }

        var id = Guid.NewGuid().ToString("D");
        UpdateSettings(settings => settings with
        {
            Profiles = settings.Profiles.Append(new ListeningProfile(id, name,
                ListeningPreferences.Capture(settings))).ToArray(),
            SelectedProfileId = id
        }, applyAudio: false);
    }

    public void UpdateSelectedProfile()
    {
        var id = Current.Settings.SelectedProfileId;
        if (!RequirePersonalProfile(id)) return;
        UpdateSettings(settings => settings with
        {
            Profiles = settings.Profiles.Select(profile => profile.Id == id
                ? profile with { Preferences = ListeningPreferences.Capture(settings) } : profile).ToArray()
        }, applyAudio: false);
    }

    public void RenameSelectedProfile(string name)
    {
        var id = Current.Settings.SelectedProfileId;
        if (!RequirePersonalProfile(id)) return;
        name = name.Trim();
        if (!AcceptProfileName(name, id)) return;
        UpdateSettings(settings => settings with
        {
            Profiles = settings.Profiles.Select(profile => profile.Id == id
                ? profile with { Name = name } : profile).ToArray()
        }, applyAudio: false);
    }

    public void DeleteSelectedProfile()
    {
        var id = Current.Settings.SelectedProfileId;
        if (!RequirePersonalProfile(id)) return;
        UpdateSettings(settings => settings with
        {
            Profiles = settings.Profiles.Where(profile => profile.Id != id).ToArray(),
            SelectedProfileId = string.Empty
        }, applyAudio: false);
    }

    private bool RequirePersonalProfile(string id)
    {
        if (Current.Settings.Profiles.Any(profile => profile.Id == id)) return true;
        ReportViewError("Select a personal profile to manage it. Use Save as new to customize a preset.");
        return false;
    }

    private bool AcceptProfileName(string name, string? exceptId = null)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 40 || name.Any(char.IsControl))
        { ReportViewError("Enter a profile name between 1 and 40 characters."); return false; }
        if (Current.Settings.Profiles.Any(profile => profile.Id != exceptId &&
            profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        { ReportViewError("A profile with this name already exists."); return false; }
        return true;
    }

    /// <summary>Stops capture and requests safety restoration. Suspension exists only in runtime state.</summary>
    public Task PauseAsync(int minutes) => RunOperationAsync(async () =>
    {
        if (minutes is not (5 or 15 or 30)) throw new ArgumentOutOfRangeException(nameof(minutes));
        if (!_engine.State.Running) throw new InvalidOperationException("Enable monitoring before pausing.");
        await _engine.SetMonitoringAsync(false);
        Change(state => state with
        {
            PauseDeadlineTick = Environment.TickCount64 + minutes * 60_000L,
            PauseSecondsRemaining = minutes * 60,
            MonitoringReminderPending = false
        });
    });

    public Task ResumePauseAsync() => RunOperationAsync(() => ResumePauseCoreAsync(null));

    private async Task ResumeExpiredPauseAsync(long deadline)
    {
        try { await RunOperationAsync(() => ResumePauseCoreAsync(deadline)); }
        finally { Interlocked.Exchange(ref _pauseResumeQueued, 0); }
    }

    /// <summary>A cancelled deadline cannot restart capture; every resume rechecks access and restoration.</summary>
    private async Task ResumePauseCoreAsync(long? expectedDeadline)
    {
        if (!Current.IsPaused || (expectedDeadline.HasValue && Current.PauseDeadlineTick != expectedDeadline)) return;
        ClearPause();
        await _licensing.RefreshAsync();
        if (!_licensing.CanUseApp)
            throw new InvalidOperationException("Monitoring remains off because the license could not be verified.");
        if (!_engine.State.Ready || _engine.State.Restoring || _engine.State.Error is not null)
            throw new InvalidOperationException("Monitoring remains off. Resolve the audio or restoration error, then press Enable.");
        await _engine.SetMonitoringAsync(true);
    }

    private void ClearPause() => Change(state => state with { PauseDeadlineTick = null, PauseSecondsRemaining = 0 });
}
