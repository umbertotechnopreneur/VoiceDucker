// VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.

using Microsoft.UI.Xaml;
using VoiceDucker.Audio;
using VoiceDucker.Licensing;

namespace VoiceDucker.Model;

/// <summary>
/// Owns application state, persistence and commands. The view renders Current and sends intent.
/// Core Audio and microphone work belong to the model's single audio worker.
/// </summary>
internal sealed class VoiceDuckerModel : IDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DuckingEngine _engine;
    private readonly StoreLicenseService _licensing;
    private readonly Timer _licenseTimer;
    private ApplicationState _current;
    private bool _trialNoticeShown;
    private bool _disposed;

    public VoiceDuckerModel(Window owner)
    {
        var settings = SettingsStore.Load(out var error);
        _engine = new DuckingEngine(settings);
        _licensing = new StoreLicenseService(owner);
        _current = new ApplicationState(settings, EngineStatus.Initial(settings), true, false, true,
            ReadLicense(), error ?? string.Empty);
        _engine.StatusChanged += OnAudioChanged;
        _licensing.Changed += OnLicenseChanged;
        _licenseTimer = new Timer(_ => owner.DispatcherQueue.TryEnqueue(CheckLicenseExpiration), null,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public ApplicationState Current { get { lock (_sync) return _current; } }
    public event Action<ApplicationState>? StateChanged;

    public async Task InitializeAsync()
    {
        await RunOperationAsync(async () =>
        {
            try
            {
                await StartupRegistration.InitializeDefaultAsync();
                var enabled = await StartupRegistration.IsEnabledAsync();
                Change(state => state with { WindowsStartupEnabled = enabled });
            }
            catch (Exception exception) { ReportViewError($"Windows startup status could not be read: {exception.Message}"); }
            finally { Change(state => state with { WindowsStartupBusy = false }); }

            await _engine.InitializeAsync();
            await _licensing.RefreshAsync();
            if (_licensing.IsStoreEdition)
                _licenseTimer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

            // This explicit opt-in applies to every launch, after snapshot and license checks.
            if (Current.Settings.ShouldEnableMicrophoneOnLaunch() && _licensing.CanUseApp)
                await _engine.SetMonitoringAsync(true);
        });
        _ = RemindWhenOffAsync();
    }

    public Task ToggleMonitoringAsync() => RunOperationAsync(async () =>
    {
        var enabled = !_engine.State.Running;
        if (enabled)
        {
            // Enable is also the recovery command after a failed initial playback snapshot.
            if (!_engine.State.Ready) await _engine.InitializeAsync();
            await _licensing.RefreshAsync();
            if (!_licensing.CanUseApp)
                throw new InvalidOperationException("An active Microsoft Store license is required. Check the license or buy VoiceDucker.");
        }
        await _engine.SetMonitoringAsync(enabled);
    });

    public void SetNumericSetting(SettingKey key, double value)
    {
        if (!double.IsFinite(value) || (key != SettingKey.ThresholdDecibels && value != Math.Round(value)) ||
            value < int.MinValue || value > int.MaxValue)
        {
            ReportViewError("Enter a number within the setting's allowed range.");
            return;
        }
        UpdateSettings(settings => key switch
        {
            SettingKey.ReductionPercent => settings with { ReductionPercent = (int)value },
            SettingKey.FadeDownMilliseconds => settings with { FadeDownMilliseconds = (int)value },
            SettingKey.FadeUpMilliseconds => settings with { FadeUpMilliseconds = (int)value },
            SettingKey.RestoreDelayMilliseconds => settings with { RestoreDelayMilliseconds = (int)value },
            SettingKey.HysteresisDecibels => settings with { HysteresisDecibels = (int)value },
            SettingKey.SensitivityDecibels => settings with { MicrophoneGainDecibels = (int)value },
            SettingKey.ThresholdDecibels => settings with { ThresholdDecibels = value },
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        });
    }

    public void SetIncludeOtherSources(bool include) =>
        UpdateSettings(settings => settings with { IncludeOtherSources = include });
    public void ResetSetting(SettingKey key)
    {
        var defaults = new DuckingSettings();
        SetNumericSetting(key, key switch
        {
            SettingKey.ReductionPercent => defaults.ReductionPercent,
            SettingKey.FadeDownMilliseconds => defaults.FadeDownMilliseconds,
            SettingKey.FadeUpMilliseconds => defaults.FadeUpMilliseconds,
            SettingKey.RestoreDelayMilliseconds => defaults.RestoreDelayMilliseconds,
            SettingKey.HysteresisDecibels => defaults.HysteresisDecibels,
            SettingKey.SensitivityDecibels => defaults.MicrophoneGainDecibels,
            SettingKey.ThresholdDecibels => defaults.ThresholdDecibels,
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        });
    }
    public void SetMicrophoneOnStartup(bool enabled) =>
        UpdateSettings(settings => settings with { EnableMicrophoneOnWindowsStartup = enabled }, applyAudio: false);
    public void SetSettingsSection(string section) =>
        UpdateSettings(settings => settings with { LastSettingsSection = section }, applyAudio: false);

    public Task SelectMicrophoneAsync(string deviceId) => RunOperationAsync(async () =>
    {
        if (!_licensing.CanUseApp && _engine.State.Running)
            await _engine.SetMonitoringAsync(false);
        var settings = UpdateSettings(current => current with { MicrophoneDeviceId = deviceId }, applyAudio: false);
        await _engine.UpdateSettingsAsync(settings);
    });
    public Task RefreshMicrophonesAsync() => ObserveAsync(_engine.RefreshMicrophonesAsync());
    public Task SetPlaybackVolumeAsync(string key, double percent)
    {
        if (!double.IsFinite(percent) || percent is < 0 or > 100)
        {
            ReportViewError("Session volume must be between 0 and 100%.");
            return Task.CompletedTask;
        }
        return ObserveAsync(_engine.SetPlaybackVolumeAsync(key, (float)(percent / 100)));
    }

    public void SetProcessHidden(string processName, bool hidden)
    {
        var name = PlaybackIdentity.NormalizeProcessName(processName);
        UpdateSettings(settings => settings with
        {
            BlacklistedProcesses = (hidden
                ? settings.BlacklistedProcesses.Append(name)
                : settings.BlacklistedProcesses.Where(item => !PlaybackIdentity.NormalizeProcessName(item)
                    .Equals(name, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        });
    }

    public async Task SetWindowsStartupAsync(bool enabled)
    {
        if (Current.WindowsStartupBusy) return;
        Change(state => state with { WindowsStartupBusy = true });
        try
        {
            await StartupRegistration.SetEnabledAsync(enabled);
            StartupRegistration.SavePreferenceMarker();
            var actual = await StartupRegistration.IsEnabledAsync();
            Change(state => state with { WindowsStartupEnabled = actual, Feedback = string.Empty });
        }
        catch (Exception exception) { ReportViewError($"Windows startup could not be changed: {exception.Message}"); }
        finally { Change(state => state with { WindowsStartupBusy = false }); }
    }

    public Task RefreshLicenseAsync() => ObserveAsync(_licensing.RefreshAsync());
    public Task PurchaseAsync() => RunOperationAsync(async () =>
    {
        var message = await _licensing.PurchaseAsync();
        if (message is not null) ReportViewError(message);
    });
    public void DismissMonitoringReminder() => Change(state => state with { MonitoringReminderPending = false });
    public void DismissTrialExpiredNotice()
    {
        _trialNoticeShown = true;
        Change(state => state with { TrialExpiredNoticePending = false });
    }
    public void ReportViewError(string message) => Change(state => state with { Feedback = message });

    public WindowPlacementState? LoadWindowPlacement() => WindowPlacementStore.Load();
    public void SaveWindowPlacement(WindowPlacementState state) => WindowPlacementStore.Save(state);

    private DuckingSettings UpdateSettings(Func<DuckingSettings, DuckingSettings> update, bool applyAudio = true)
    {
        DuckingSettings next;
        lock (_sync)
        {
            next = update(_current.Settings);
            if (!next.IsValid)
            {
                _current = _current with { Feedback = "The setting is outside its allowed range." };
                next = _current.Settings;
            }
            else
            {
                _current = _current with { Settings = next, Feedback = string.Empty };
                // Accepted state, worker command order and persisted preferences share one owner.
                if (applyAudio) _ = ObserveAsync(_engine.UpdateSettingsAsync(next));
                try { SettingsStore.Save(next); }
                catch (Exception exception)
                {
                    _current = _current with { Feedback = $"Settings are active but could not be saved: {exception.Message}" };
                }
            }
        }
        Publish();
        return next;
    }

    private void OnAudioChanged(EngineStatus audio) => Change(state => state with { Audio = audio });
    private void CheckLicenseExpiration()
    {
        if (_disposed) return;
        _licensing.CheckExpiration();
        OnLicenseChanged();
    }
    private void OnLicenseChanged()
    {
        if (_disposed) return;
        Change(state => state with
        {
            License = ReadLicense(),
            TrialExpiredNoticePending = !_trialNoticeShown && _licensing.IsTrialLicense &&
                                        _licensing.State == LicenseState.Expired
        });
        if (!_licensing.CanUseApp)
            _ = RunOperationAsync(async () =>
            {
                // Recheck after earlier operations, including an enable still in progress.
                if (!_licensing.CanUseApp && _engine.State.Running)
                    await _engine.SetMonitoringAsync(false);
            });
    }
    private LicenseViewState ReadLicense()
    {
        var now = DateTimeOffset.UtcNow;
        var state = _licensing.State;
        var days = Math.Max(1, (int)Math.Ceiling((_licensing.Expires - now).TotalDays));
        var text = state switch
        {
            LicenseState.Trial => $"Free trial — {days} days remaining. Ends {_licensing.Expires.LocalDateTime:d}. Purchase is optional; no automatic charge.",
            LicenseState.Expired => "Your trial or license has ended. Buy VoiceDucker to keep using it.",
            LicenseState.Checking => "Checking your Microsoft Store license…",
            LicenseState.Unavailable => "Your Microsoft Store license could not be checked. Please retry.",
            _ => string.Empty
        };
        return new LicenseViewState(_licensing.IsStoreEdition, _licensing.CanUseApp,
            LicensePolicy.WindowTitle("VoiceDucker", state, _licensing.Expires, now),
            _licensing.IsStoreEdition && state != LicenseState.Full,
            state == LicenseState.Trial ? "You're using the trial version" : state == LicenseState.Expired
                ? "Trial expired" : "Microsoft Store edition",
            _licensing.IsStoreEdition && state is LicenseState.Checking or LicenseState.Unavailable, text);
    }

    private async Task RunOperationAsync(Func<Task> operation)
    {
        if (_disposed) return;
        try { await _operation.WaitAsync(_lifetime.Token); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
        Change(state => state with { Busy = true });
        try { await operation(); }
        catch (Exception exception) { ReportViewError(exception.Message); }
        finally
        {
            Change(state => state with { Busy = false });
            _operation.Release();
        }
    }
    private async Task ObserveAsync(Task operation)
    {
        try { await operation; }
        catch (Exception exception) { ReportViewError(exception.Message); }
    }
    private async Task RemindWhenOffAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), _lifetime.Token);
            if (!_engine.State.Running) Change(state => state with { MonitoringReminderPending = true });
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }
    private void Change(Func<ApplicationState, ApplicationState> update)
    {
        lock (_sync)
        {
            if (_disposed) return;
            _current = update(_current);
        }
        Publish();
    }
    private void Publish()
    {
        if (!_disposed) StateChanged?.Invoke(Current);
    }
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _lifetime.Cancel();
        _licenseTimer.Dispose();
        _licensing.Changed -= OnLicenseChanged;
        _engine.StatusChanged -= OnAudioChanged;
        try { _engine.Dispose(); }
        finally { _licensing.Dispose(); _lifetime.Dispose(); }
    }
}
