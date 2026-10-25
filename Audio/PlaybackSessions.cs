// VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.

namespace VoiceDucker.Audio;

/// <summary>Immutable presentation state separating the user's normal level from actual mixer readback.</summary>
internal sealed record PlaybackStream(string Key, string Name, int ProcessId,
    float NormalVolume, float ActualVolume, float TargetVolume, float Peak,
    bool Muted, bool IsSpotify, bool IsLowered, bool IsEligible);

/// <summary>A scalar transport observation for one endpoint/session instance at discovery time.</summary>
internal sealed record PlaybackObservation(string Key, string Name, int ProcessId,
    float Volume, float Peak, bool Muted, bool Active);

/// <summary>Keeps playback observations and individual errors, indicating whether device enumeration completed.</summary>
internal sealed record PlaybackReadResult(IReadOnlyList<PlaybackObservation> Sessions,
    IReadOnlyList<string> Errors, bool Enumerated = true);

/// <summary>Worker-owned transport. The controller and its fake share the same scalar contract.</summary>
internal interface IPlaybackSource : IDisposable
{
    /// <summary>Discovers current sessions without changing their playback state.</summary>
    PlaybackReadResult Read();

    /// <summary>Reads a fresh native scalar for validation of an external volume event.</summary>
    float ReadVolume(string key);

    /// <summary>Writes a session scalar and returns its confirmed readback.</summary>
    float SetVolume(string key, float volume);
}

/// <summary>
/// Owns normal levels, immutable startup restoration, and fades on one audio worker.
/// No level observed during our own fade is ever adopted as a new normal volume.
/// </summary>
internal sealed class PlaybackSessions(IPlaybackSource source, PlaybackVolumeEvents volumeEvents,
    Func<long>? clock = null) : IDisposable
{
    #region Private fields

    private const float AudiblePeak = 0.001f;
    private const float VerificationTolerance = 0.0001f;
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly Func<long> _clock = clock ?? (() => Environment.TickCount64);
    private string[] _errors = [];
    private PlaybackStream[] _streams = [];

    #endregion

    #region Public state

    /// <summary>Active, visible playback sessions from the most recent update.</summary>
    public IReadOnlyList<PlaybackStream> Streams => _streams;

    /// <summary>Distinct discovery, control and verification failures from the most recent update.</summary>
    public IReadOnlyList<string> Errors => _errors;

    /// <summary>Whether an owned session still needs its immutable startup volume restored.</summary>
    public bool HasPendingRestoration => _sessions.Values.Any(session => session.RestoreStartup);

    /// <summary>Whether a pending restoration currently has an accessible, unmuted session to advance.</summary>
    public bool CanAdvanceRestoration => _sessions.Values.Any(session =>
        session.RestoreStartup && session.Present && !session.Muted);

    /// <summary>Explains deferred restoration instead of claiming that unavailable audio was restored.</summary>
    public string? RestorationError => HasPendingRestoration
        ? $"Startup volume restoration is pending for {_sessions.Values.Count(session => session.RestoreStartup)} session(s), including muted or unavailable playback."
        : null;

    #endregion

    #region Public methods

    /// <summary>Accepts a newer normal-level intent without replacing the immutable startup volume.</summary>
    public void SetNormalVolume(string key, float volume, long? intentVersion = null)
    {
        if (!float.IsFinite(volume) || volume is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(volume));
        if (!_sessions.TryGetValue(key, out var session) || !session.Present)
            throw new InvalidOperationException("This audio stream has ended.");

        var version = intentVersion ?? volumeEvents.NextIntentVersion();
        if (version <= session.LatestIntentVersion) return;

        CompletePending(session);
        session.LatestIntentVersion = version;
        session.Normal = volume;
        session.ApplyNormal = true;
        session.RestoreStartup = false;
        session.AutomaticReductionApplied = false;

        AudioDiagnostics.Write(() => $"normal-volume-command session={key} normal={volume:R}");
    }

    /// <summary>
    /// Queues a newer actual-volume intent for conversion with its update's gate and settings.
    /// Replaced intents complete without applying an older slider position.
    /// </summary>
    public Task SetPlaybackVolume(string key, float actual, long? intentVersion = null)
    {
        if (!float.IsFinite(actual) || actual is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(actual));
        if (!_sessions.TryGetValue(key, out var session) || !session.Present)
            throw new InvalidOperationException("This audio stream has ended.");

        var version = intentVersion ?? volumeEvents.NextIntentVersion();
        if (version <= session.LatestIntentVersion) return Task.CompletedTask;

        CompletePending(session);
        session.LatestIntentVersion = version;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.PendingPlayback = new PlaybackEdit(actual, completion);

        return completion.Task;
    }

    /// <summary>Resumes normal-level control without an intermediate startup-volume jump.</summary>
    public void MonitoringEnabled()
    {
        foreach (var session in _sessions.Values)
        {
            // A new explicit Enable resumes operational control without a transient
            // startup-volume bump when an old muted/unavailable restore becomes possible.
            session.ApplyNormal = session.RestoreStartup || session.Normal != session.Startup;
            session.RestoreStartup = false;
            session.FadeValid = false;
        }
    }

    /// <summary>
    /// Cancels pending edits and schedules startup restoration only for sessions the application owned.
    /// Muted or unavailable sessions retain that obligation until restoration can be confirmed.
    /// </summary>
    public void RequestStartupRestoration(int fadeMilliseconds = 0)
    {
        foreach (var session in _sessions.Values)
        {
            CompletePending(session, new InvalidOperationException("Playback volume editing was interrupted because monitoring stopped."));
            session.ApplyNormal = false;
            session.Automatic = false;
            session.AutomaticReductionApplied = false;
            if (!session.ChangedByApp && !session.RestoreStartup) continue;

            session.RestoreStartup = true;
            session.RestoreMilliseconds = fadeMilliseconds;
            session.FadeValid = false;
        }
    }

    /// <summary>Discovers, consumes external intent, advances eligible fades and publishes readback.</summary>
    public bool Update(bool running, bool speaking, DuckingSettings settings)
    {
        var now = _clock();
        var errors = new List<string>();
        PlaybackReadResult read;

        try
        {
            read = source.Read();
        }
        catch (Exception exception)
        {
            foreach (var session in _sessions.Values)
                CompletePending(session, new InvalidOperationException("Playback volume could not be changed because playback could not be refreshed.", exception));

            _streams = [];
            _errors = [$"Playback discovery failed: {exception.Message}"];
            return false;
        }

        errors.AddRange(read.Errors);

        foreach (var session in _sessions.Values)
            session.Present = false;

        foreach (var observation in read.Sessions)
        {
            if (!float.IsFinite(observation.Volume)) continue;

            if (!_sessions.TryGetValue(observation.Key, out var session))
            {
                session = new Session(observation);
                _sessions.Add(observation.Key, session);
                AudioDiagnostics.Write(() => $"startup-session session={session.Key} process={session.Name} original={session.Startup:R}");
            }

            var observedVolume = observation.Volume;

            // A delayed foreign callback must still describe the current native level.
            // The event payload supplies intent; fresh readback rejects superseded echoes.
            var external = volumeEvents.Latest(session.Key);
            if (external is not null && external.Version > session.ExternalVersion &&
                external.Version > session.LatestIntentVersion)
            {
                try
                {
                    observedVolume = source.ReadVolume(session.Key);
                }
                catch (Exception exception)
                {
                    errors.Add($"{session.Name}: {exception.Message}");
                    continue;
                }

                session.ExternalVersion = external.Version;
                if (external.Volume == observedVolume && external.Volume != session.Actual && !session.RestoreStartup)
                {
                    CompletePending(session);
                    session.LatestIntentVersion = external.Version;
                    session.Normal = Math.Clamp(external.Volume, 0, 1);
                    session.ApplyNormal = true;
                    session.AutomaticReductionApplied = false;
                    AudioDiagnostics.Write(() => $"external-normal-volume session={session.Key} normal={session.Normal:R} muted={external.Muted}");
                }
            }

            session.Present = true;
            session.Name = observation.Name;
            session.ProcessId = observation.ProcessId;
            session.Actual = Math.Clamp(observedVolume, 0, 1);
            session.Peak = float.IsFinite(observation.Peak) ? Math.Clamp(observation.Peak, 0, 1) : 0;
            session.Muted = observation.Muted;
            session.Active = observation.Active;
            session.Eligible = !settings.IsProcessExcluded(session.Name) &&
                (settings.IncludeOtherSources || session.IsSpotify);

            try
            {
                ApplyPendingPlayback(session, running, speaking, settings, now);
                Advance(session, running, speaking, settings, now);
                session.LastError = null;
            }
            catch (Exception exception)
            {
                // Preserve ownership: a failed write/readback must still be restored later.
                errors.Add($"{session.Name}: {exception.Message}");
                if (session.LastError != exception.Message)
                    AudioDiagnostics.Write(() => $"session-error session={session.Key} error={exception.Message}");

                session.LastError = exception.Message;
            }
        }

        foreach (var session in _sessions.Values.Where(session => !session.Present))
            CompletePending(session, new InvalidOperationException("This audio stream ended or became unavailable before its volume could be changed."));

        _streams = _sessions.Values.Where(session => session.Present && session.Active &&
                !PlaybackIdentity.IsOwnProcess(session.Name))
            .Select(session => new PlaybackStream(session.Key, session.Name, session.ProcessId,
                session.Normal, session.Actual, session.Target, session.Peak, session.Muted,
                session.IsSpotify, running && speaking && session.Eligible && !session.Muted &&
                session.AutomaticReductionApplied && session.Actual < session.Normal,
                session.Eligible)).ToArray();
        _errors = errors.Distinct(StringComparer.Ordinal).ToArray();

        return read.Enumerated;
    }

    /// <summary>Settles pending edits and releases the worker-owned transport.</summary>
    public void Dispose()
    {
        foreach (var session in _sessions.Values)
            CompletePending(session, new ObjectDisposedException(nameof(PlaybackSessions)));

        source.Dispose();
    }

    #endregion

    #region Implementation

    /// <summary>
    /// Inverts an actual slider position into a normal level using this update's effective gain.
    /// Invalid, fully reduced, muted or silent edits fail without changing the startup baseline.
    /// </summary>
    private void ApplyPendingPlayback(Session session, bool running, bool speaking,
        DuckingSettings settings, long now)
    {
        var edit = session.PendingPlayback;
        if (edit is null) return;

        session.PendingPlayback = null;

        try
        {
            var ducking = running && speaking && session.Eligible && settings.ReductionPercent > 0;
            var effectiveGain = ducking ? 1f - settings.ReductionPercent / 100f : 1f;

            if (effectiveGain == 0)
                throw new InvalidOperationException("Playback volume cannot be changed while reduction is 100%. Reduce the reduction amount first.");
            if (session.Muted)
                throw new InvalidOperationException("Playback volume cannot be changed while this audio stream is muted.");
            if (!session.Active || session.Peak <= AudiblePeak)
                throw new InvalidOperationException("Playback volume cannot be changed while this audio stream is silent. Resume playback first.");

            session.Normal = Math.Clamp(edit.Actual / effectiveGain, 0, 1);
            session.RestoreStartup = false;
            session.ApplyNormal = false;
            session.Automatic = ducking;
            session.AutomaticReductionApplied = false;

            ApplyTarget(session, session.Normal * effectiveGain, 0, now, ducking);
            AudioDiagnostics.Write(() => $"playback-volume-command session={session.Key} requested={edit.Actual:R} normal={session.Normal:R} actual={session.Actual:R}");
            edit.Completion.TrySetResult();
        }
        catch (Exception exception)
        {
            edit.Completion.TrySetException(exception);
            throw;
        }
    }

    /// <summary>Detaches and settles one queued actual edit after replacement, cancellation or failure.</summary>
    private static void CompletePending(Session session, Exception? exception = null)
    {
        var pending = session.PendingPlayback;
        session.PendingPlayback = null;

        if (exception is null) pending?.Completion.TrySetResult();
        else pending?.Completion.TrySetException(exception);
    }

    /// <summary>
    /// Chooses startup restoration, explicit normal projection, voice reduction or normal fade-back
    /// in that priority order. New reductions require audible playback and never change mute state.
    /// </summary>
    private void Advance(Session session, bool running, bool speaking, DuckingSettings settings, long now)
    {
        if (session.Muted) return; // Deferred restoration never unmutes or writes a muted session.

        if (session.RestoreStartup)
        {
            ApplyTarget(session, session.Startup, session.RestoreMilliseconds, now, false);
            if (session.Completed)
            {
                session.RestoreStartup = false;
                session.ChangedByApp = false;
                session.Automatic = false;
                AudioDiagnostics.Write(() => $"restore-startup-complete session={session.Key} target={session.Startup:R} actual={session.Actual:R}");
            }

            return;
        }

        var audible = session.Active && session.Peak > AudiblePeak;
        var ducking = running && speaking && session.Eligible && settings.ReductionPercent > 0;

        if (session.ApplyNormal)
        {
            // An explicit normal-volume command has a single deterministic projection.
            // It waits for audible playback instead of writing a silent session.
            if (!audible) return;

            var target = ducking
                ? session.Normal * (1f - settings.ReductionPercent / 100f)
                : session.Normal;
            session.Automatic = ducking;
            ApplyTarget(session, target, 0, now, ducking);
            session.ApplyNormal = false;
            return;
        }

        if (ducking)
        {
            if (!audible) return;

            session.Automatic = true;
            ApplyTarget(session, session.Normal * (1f - settings.ReductionPercent / 100f),
                settings.FadeDownMilliseconds, now, true, settings.SmoothTransitions);
        }
        else if (session.Automatic)
        {
            // Return only sessions previously controlled by the app, including a now-silent tail.
            ApplyTarget(session, session.Normal, settings.FadeUpMilliseconds, now, false, settings.SmoothTransitions);
            if (session.Completed)
            {
                session.Automatic = false;
                session.AutomaticReductionApplied = false;
            }
        }
    }

    /// <summary>
    /// Advances a fade from confirmed actual volume and verifies each changed native write.
    /// A settled target acknowledges quantized readback so polling cannot repeatedly write it.
    /// </summary>
    private void ApplyTarget(Session session, float target, int duration, long now, bool ducking, bool smooth = false)
    {
        target = Math.Clamp(target, 0, 1);

        if (!session.FadeValid || session.Target != target ||
            (!session.Completed && (session.Duration != duration || session.Smooth != smooth)))
        {
            session.Target = target;
            session.Start = session.Actual;
            session.StartTick = now;
            session.Duration = session.Actual == target ? 0 : duration;
            session.Smooth = smooth;
            session.FadeValid = true;
            session.Completed = false;
            session.Acknowledged = false;
            AudioDiagnostics.Write(() => $"fade-start process={session.Name} session={session.Key} normal={session.Normal:R} actual={session.Actual:R} target={target:R} duration={session.Duration}");
        }

        var fadeProgress = session.Duration == 0 ? 1f
            : Math.Clamp((float)(now - session.StartTick) / session.Duration, 0, 1);
        // Smoothstep keeps both ends gentle while retargeting always starts at confirmed actual volume.
        var shapedProgress = session.Smooth ? fadeProgress * fadeProgress * (3 - 2 * fadeProgress) : fadeProgress;
        var requestedVolume = fadeProgress >= 1 ? target : session.Start + (target - session.Start) * shapedProgress;
        var targetAlreadyConfirmed = fadeProgress >= 1 && session.Acknowledged &&
            session.Actual == session.AcknowledgedActual;

        if (session.Actual != requestedVolume && !targetAlreadyConfirmed)
        {
            // Own before writing: even a successful write followed by failed readback needs cleanup.
            session.ChangedByApp = true;
            var actual = source.SetVolume(session.Key, requestedVolume);
            if (!float.IsFinite(actual) || Math.Abs(actual - requestedVolume) >= VerificationTolerance)
                throw new InvalidOperationException("Windows did not confirm the requested session volume.");

            session.Actual = actual;
            session.AcknowledgedActual = actual;
            if (ducking && actual < session.Normal)
                session.AutomaticReductionApplied = true;
        }

        if (fadeProgress >= 1)
        {
            if (ducking && session.ChangedByApp && session.Actual < session.Normal)
                session.AutomaticReductionApplied = true;
            if (!session.Completed)
                AudioDiagnostics.Write(() => $"fade-complete process={session.Name} session={session.Key} normal={session.Normal:R} target={target:R} actual={session.Actual:R}");

            session.AcknowledgedActual = session.Actual;
            session.Acknowledged = true;
            session.Completed = true;
        }
    }

    #endregion

    #region Session state

    /// <summary>A queued actual-level request and its asynchronous command completion.</summary>
    private sealed record PlaybackEdit(float Actual, TaskCompletionSource Completion);

    /// <summary>
    /// Worker-owned lifetime state for one endpoint/session instance. Startup is immutable;
    /// normal changes only through explicit intent, while actual and fade state follow readback.
    /// </summary>
    private sealed class Session(PlaybackObservation initial)
    {
        #region Identity and volume references

        public string Key { get; } = initial.Key;
        public float Startup { get; } = Math.Clamp(initial.Volume, 0, 1);
        public float Normal { get; set; } = Math.Clamp(initial.Volume, 0, 1);
        public float Actual { get; set; } = Math.Clamp(initial.Volume, 0, 1);
        public float Target { get; set; } = Math.Clamp(initial.Volume, 0, 1);
        public string Name { get; set; } = initial.Name;
        public int ProcessId { get; set; } = initial.ProcessId;
        public bool IsSpotify => Name.StartsWith("Spotify", StringComparison.OrdinalIgnoreCase);

        #endregion

        #region Latest observation

        public bool Active { get; set; }
        public bool Present { get; set; }
        public bool Muted { get; set; }
        public float Peak { get; set; }
        public bool Eligible { get; set; }

        #endregion

        #region Ownership and intent

        public bool ChangedByApp { get; set; }
        public bool RestoreStartup { get; set; }
        public int RestoreMilliseconds { get; set; }
        public bool ApplyNormal { get; set; }
        public bool Automatic { get; set; }
        public bool AutomaticReductionApplied { get; set; }
        public long ExternalVersion { get; set; }
        public long LatestIntentVersion { get; set; }
        public PlaybackEdit? PendingPlayback { get; set; }

        #endregion

        #region Fade and verification

        public float Start { get; set; }
        public long StartTick { get; set; }
        public int Duration { get; set; }
        public bool Smooth { get; set; }
        public bool FadeValid { get; set; }
        public bool Completed { get; set; }
        public bool Acknowledged { get; set; }
        public float AcknowledgedActual { get; set; }
        public string? LastError { get; set; }

        #endregion
    }

    #endregion
}
