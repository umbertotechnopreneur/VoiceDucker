# VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.
# Exercises the production application model against in-memory platform adapters.
# Never opens WinUI, Core Audio, the registry, user settings, or Microsoft Store.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$testRoot = Join-Path $projectRoot 'artifacts\application-model-tests'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$sourceRoot = [Security.SecurityElement]::Escape($projectRoot)
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Program.cs" />
    <Compile Include="Fakes.cs" />
    <Compile Include="$sourceRoot\Model\ApplicationState.cs" Link="ApplicationState.cs" />
    <Compile Include="$sourceRoot\Model\VoiceDuckerModel.cs" Link="VoiceDuckerModel.cs" />
    <Compile Include="$sourceRoot\Model\VoiceDuckerModel.Listening.cs" Link="VoiceDuckerModel.Listening.cs" />
    <Compile Include="$sourceRoot\Audio\EngineStatus.cs" Link="EngineStatus.cs" />
    <Compile Include="$sourceRoot\Audio\DuckingSettings.cs" Link="DuckingSettings.cs" />
    <Compile Include="$sourceRoot\Audio\ListeningProfile.cs" Link="ListeningProfile.cs" />
    <Compile Include="$sourceRoot\Audio\PlaybackIdentity.cs" Link="PlaybackIdentity.cs" />
    <Compile Include="$sourceRoot\Audio\VoiceGate.cs" Link="VoiceGate.cs" />
    <Compile Include="$sourceRoot\Licensing\LicensePolicy.cs" Link="LicensePolicy.cs" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $testRoot 'ModelTests.csproj') -Encoding utf8

@'
using VoiceDucker.Audio;
using VoiceDucker.Licensing;

internal static class FakeHost
{
    public static readonly List<string> Trace = [];
    public static void Reset(DuckingSettings? settings = null, bool windowsStartup = false,
        LicenseState license = LicenseState.Unrestricted)
    {
        Trace.Clear();
        VoiceDucker.SettingsStore.Saved = settings ?? new DuckingSettings();
        VoiceDucker.SettingsStore.SaveCount = 0;
        VoiceDucker.SettingsStore.LoadError = null;
        VoiceDucker.SettingsStore.FailSave = false;
        VoiceDucker.StartupRegistration.Enabled = windowsStartup;
        VoiceDucker.StartupRegistration.ChangeCount = 0;
        VoiceDucker.StartupRegistration.Fail = false;
        VoiceDucker.WindowPlacementStore.Saved = null;
        StoreLicenseService.InitialState = license;
        StoreLicenseService.StoreEdition = license != LicenseState.Unrestricted;
    }
}
namespace Microsoft.UI.Dispatching
{
    internal sealed class DispatcherQueue
    {
        public bool TryEnqueue(Action callback) { callback(); return true; }
    }
}
namespace Microsoft.UI.Xaml
{
    internal sealed class Window
    {
        public Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; } = new();
    }
}
namespace VoiceDucker
{
    internal static class SettingsStore
    {
        public static DuckingSettings Saved = new();
        public static int SaveCount;
        public static string? LoadError;
        public static bool FailSave;
        public static DuckingSettings Load(out string? error) { error = LoadError; return Saved; }
        public static void Save(DuckingSettings settings)
        {
            if (FailSave) throw new IOException("synthetic settings write failure");
            Saved = settings; SaveCount++;
        }
    }
    internal static class StartupRegistration
    {
        public static bool Enabled;
        public static bool Fail;
        public static int ChangeCount;
        public static Task InitializeDefaultAsync() => Task.CompletedTask;
        public static Task<bool> IsEnabledAsync() => Task.FromResult(Enabled);
        public static Task SetEnabledAsync(bool enabled)
        {
            ChangeCount++;
            if (Fail) throw new InvalidOperationException("synthetic startup registration failure");
            Enabled = enabled; return Task.CompletedTask;
        }
        public static void SavePreferenceMarker() { }
    }
    internal sealed record WindowPlacementState(int X, int Y, bool SettingsPaneOpen);
    internal static class WindowPlacementStore
    {
        public static WindowPlacementState? Saved;
        public static WindowPlacementState? Load() => Saved;
        public static void Save(WindowPlacementState state) => Saved = state;
    }
}
namespace VoiceDucker.Audio
{
    internal static class MicrophoneLevel
    {
        // Required to compile the gate DTO source; the real decoder has its own synthetic harness.
        public static double ApplyGain(double rms, int decibels) => rms * Math.Pow(10, decibels / 20d);
    }
    internal sealed record MicrophoneChoice(string Id, string Name);
    internal sealed record PlaybackStream(string Key, string Name, int ProcessId,
        float NormalVolume, float ActualVolume, float TargetVolume, float Peak,
        bool Muted, bool IsSpotify, bool IsLowered, bool IsEligible);
    internal sealed class DuckingEngine : IDisposable
    {
        public static DuckingEngine Last = null!;
        public DuckingSettings Settings;
        public EngineStatus State { get; private set; }
        public bool Disposed;
        public int MonitoringCalls;
        public int RefreshMicrophoneCalls;
        public int SettingsCalls;
        public int PlaybackVolumeCalls;
        public string LastVolumeKey = "";
        public float LastPlaybackVolume;
        public bool FailMonitoring;
        public bool FailInitialize;
        public bool FailSettings;
        public bool FailVolume;
        public int ActiveCommands;
        public int MaximumConcurrentCommands;
        public TaskCompletionSource? MonitoringBlock;
        public TaskCompletionSource? BeforeMonitoringBlock;
        public TaskCompletionSource? SettingsBlock;
        public event Action<EngineStatus>? StatusChanged;
        public int SubscriberCount => StatusChanged?.GetInvocationList().Length ?? 0;
        public DuckingEngine(DuckingSettings settings)
        {
            Last = this; Settings = settings; State = EngineStatus.Initial(settings);
        }
        public Task InitializeAsync()
        {
            if (FailInitialize) return Task.FromException(new InvalidOperationException("synthetic snapshot failure"));
            FakeHost.Trace.Add("snapshot");
            Publish(State with { Ready = true, Microphones = [new("", "Fake default"), new("mic-b", "Fake B")] });
            return Task.CompletedTask;
        }
        public async Task SetMonitoringAsync(bool enabled)
        {
            if (enabled && !State.Ready) throw new InvalidOperationException("capture before snapshot");
            MonitoringCalls++;
            ActiveCommands++;
            MaximumConcurrentCommands = Math.Max(MaximumConcurrentCommands, ActiveCommands);
            FakeHost.Trace.Add(enabled ? "capture-on" : "capture-off");
            try
            {
                if (enabled && BeforeMonitoringBlock is { } before) await before.Task;
                if (FailMonitoring) throw new InvalidOperationException("synthetic microphone start failure");
                Publish(State with { Running = enabled });
                if (MonitoringBlock is { } block) await block.Task;
            }
            finally { ActiveCommands--; }
        }
        public async Task UpdateSettingsAsync(DuckingSettings settings)
        {
            SettingsCalls++;
            if (SettingsBlock is { } block) await block.Task;
            if (FailSettings) throw new InvalidOperationException("synthetic audio settings failure");
            Settings = settings;
        }
        public Task RefreshMicrophonesAsync() { RefreshMicrophoneCalls++; return Task.CompletedTask; }
        public Task SetPlaybackVolumeAsync(string key, float actual)
        {
            PlaybackVolumeCalls++; LastVolumeKey = key; LastPlaybackVolume = actual;
            if (FailVolume) return Task.FromException(new InvalidOperationException("synthetic session volume failure"));
            return Task.CompletedTask;
        }
        public void Publish(EngineStatus state) { State = state; StatusChanged?.Invoke(state); }
        public void Dispose() { Disposed = true; }
    }
}
namespace VoiceDucker.Licensing
{
    internal sealed class StoreLicenseService : IDisposable
    {
        public static StoreLicenseService Last = null!;
        public static LicenseState InitialState;
        public static bool StoreEdition;
        public LicenseState State { get; private set; }
        public DateTimeOffset Expires { get; private set; } = DateTimeOffset.UtcNow.AddDays(10);
        public bool IsStoreEdition { get; }
        public bool IsTrialLicense { get; private set; }
        public bool CanUseApp => LicensePolicy.CanUse(State, Expires, DateTimeOffset.UtcNow);
        public bool Disposed;
        public int RefreshCalls;
        public event Action? Changed;
        public int SubscriberCount => Changed?.GetInvocationList().Length ?? 0;
        public StoreLicenseService(Microsoft.UI.Xaml.Window owner)
        {
            Last = this; State = InitialState; IsStoreEdition = StoreEdition;
            IsTrialLicense = State is LicenseState.Trial or LicenseState.Expired;
        }
        public Task RefreshAsync()
        {
            RefreshCalls++; FakeHost.Trace.Add("license"); Changed?.Invoke(); return Task.CompletedTask;
        }
        public Task LoadPriceAsync() => Task.CompletedTask;
        public Task<string?> PurchaseAsync() => Task.FromResult<string?>("synthetic purchase response");
        public void CheckExpiration() { }
        public void SetState(LicenseState state)
        {
            State = state; IsTrialLicense = state is LicenseState.Trial or LicenseState.Expired;
            Changed?.Invoke();
        }
        public void Dispose() { Disposed = true; }
    }
}
'@ | Set-Content -LiteralPath (Join-Path $testRoot 'Fakes.cs') -Encoding utf8

@'
using Microsoft.UI.Xaml;
using VoiceDucker;
using VoiceDucker.Audio;
using VoiceDucker.Licensing;
using VoiceDucker.Model;

var checks = 0;
var failures = new List<string>();
void Check(bool condition, string message)
{
    checks++;
    if (!condition) failures.Add(message);
}
async Task<bool> Eventually(Func<bool> condition)
{
    for (var attempt = 0; attempt < 100; attempt++)
    {
        if (condition()) return true;
        await Task.Delay(2);
    }
    return condition();
}
foreach (var startup in new[] { false, true })
foreach (var microphone in new[] { false, true })
{
    FakeHost.Reset(new DuckingSettings { EnableMicrophoneOnWindowsStartup = microphone }, startup);
    using var model = new VoiceDuckerModel(new Window());
    Check(model.Current.Busy && !model.Current.Audio.Ready, "An uninitialized model exposed ready audio commands.");
    await model.InitializeAsync();
    Check(model.Current.Audio.Ready && !model.Current.Busy, "Initialization did not finish its busy state.");
    Check(model.Current.Audio.Running == microphone,
        $"Saved microphone opt-in {microphone} was coupled to Windows startup {startup}.");
    Check(model.Current.WindowsStartupEnabled == startup, "Windows startup state was not reflected by the model.");
    Check(FakeHost.Trace.IndexOf("snapshot") < FakeHost.Trace.IndexOf("license"),
        "Licensing ran before the playback snapshot was prepared.");
    if (microphone)
        Check(FakeHost.Trace.IndexOf("license") < FakeHost.Trace.IndexOf("capture-on"),
            "Capture started before normal license checks finished.");
    else Check(!FakeHost.Trace.Contains("capture-on"), "A default-off launch opened capture.");
}

FakeHost.Reset();
using (var model = new VoiceDuckerModel(new Window()))
{
    await model.InitializeAsync();
    var engine = DuckingEngine.Last;
    model.SetMicrophoneOnStartup(true);
    Check(model.Current.Settings.EnableMicrophoneOnWindowsStartup && SettingsStore.Saved.EnableMicrophoneOnWindowsStartup,
        "The startup microphone preference did not persist.");
    Check(engine.MonitoringCalls == 0, "Editing the startup microphone checkbox immediately started capture.");
    Check(StartupRegistration.ChangeCount == 0, "Editing microphone opt-in changed Windows startup registration.");
    await model.SetWindowsStartupAsync(true);
    await model.SetWindowsStartupAsync(false);
    Check(model.Current.Settings.EnableMicrophoneOnWindowsStartup && !model.Current.WindowsStartupEnabled,
        "Disabling Windows startup cleared the independent microphone opt-in.");
    Check(engine.MonitoringCalls == 0, "Changing Windows startup started microphone capture.");

    var numeric = new[]
    {
        (SettingKey.ReductionPercent, 35d), (SettingKey.FadeDownMilliseconds, 450d),
        (SettingKey.FadeUpMilliseconds, 8500d), (SettingKey.RestoreDelayMilliseconds, 1250d),
        (SettingKey.HysteresisDecibels, 6d), (SettingKey.SensitivityDecibels, 12d),
        (SettingKey.ThresholdDecibels, -43.7)
    };
    foreach (var (key, value) in numeric) model.SetNumericSetting(key, value);
    var settings = model.Current.Settings;
    Check(settings.ReductionPercent == 35 && settings.FadeDownMilliseconds == 450 &&
        settings.FadeUpMilliseconds == 8500 && settings.RestoreDelayMilliseconds == 1250 &&
        settings.HysteresisDecibels == 6 && settings.MicrophoneGainDecibels == 12 &&
        settings.ThresholdDecibels == -43.7, "Typed numeric settings did not map to the application object model.");
    Check(SettingsStore.Saved == settings && engine.Settings == settings,
        "Saved settings and the audio worker did not receive the current model settings.");
    foreach (var (key, invalid) in new[]
    {
        (SettingKey.ReductionPercent, double.NaN), (SettingKey.ReductionPercent, 50.5),
        (SettingKey.ReductionPercent, 101d), (SettingKey.FadeDownMilliseconds, -1d),
        (SettingKey.FadeUpMilliseconds, 30001d), (SettingKey.RestoreDelayMilliseconds, 10001d),
        (SettingKey.HysteresisDecibels, 13d), (SettingKey.SensitivityDecibels, 37d),
        (SettingKey.ThresholdDecibels, -60.1), (SettingKey.ThresholdDecibels, double.PositiveInfinity)
    })
    {
        var saves = SettingsStore.SaveCount;
        model.SetNumericSetting(key, invalid);
        Check(model.Current.Settings == settings && SettingsStore.SaveCount == saves && model.Current.Feedback.Length > 0,
            $"Invalid setting {key}={invalid} was applied, persisted, or silently discarded.");
    }
    model.SetSettingsSection("volume");
    Check(model.Current.Settings.LastSettingsSection == "volume" && SettingsStore.Saved.LastSettingsSection == "volume",
        "The settings section did not remain in the application preferences.");
    model.SetIncludeOtherSources(true);
    Check(model.Current.Settings.IncludeOtherSources, "The source filter setting was not applied.");
    model.SetProcessHidden("Spotify.exe", true);
    model.SetProcessHidden("spotify", true);
    Check(model.Current.HiddenProcesses.Count == 1 && model.Current.Settings.IsProcessExcluded("Spotify"),
        "Hidden processes were not normalized and deduplicated.");
    model.SetProcessHidden("SPOTIFY.exe", false);
    Check(model.Current.HiddenProcesses.Count == 0, "Showing a hidden process did not remove its normalized entry.");
    await model.SelectMicrophoneAsync("mic-b");
    Check(model.Current.Settings.MicrophoneDeviceId == "mic-b" && engine.Settings.MicrophoneDeviceId == "mic-b",
        "The selected microphone did not pass through the model to the engine.");
    await model.RefreshMicrophonesAsync();
    Check(engine.RefreshMicrophoneCalls == 1, "Microphone refresh bypassed the engine command.");
    var monitoringCalls = engine.MonitoringCalls;
    var settingsCalls = engine.SettingsCalls;
    await model.SetPlaybackVolumeAsync("spotify-session", 42);
    Check(engine.PlaybackVolumeCalls == 1 && engine.LastVolumeKey == "spotify-session" &&
        Math.Abs(engine.LastPlaybackVolume - 0.42f) < 0.00001f,
        "The playback slider did not submit its requested actual volume unchanged to the engine.");
    Check(engine.MonitoringCalls == monitoringCalls && engine.SettingsCalls == settingsCalls,
        "A playback-volume edit changed microphone state or global ducking settings.");
    var placement = new WindowPlacementState(12, 34, true);
    model.SaveWindowPlacement(placement);
    Check(model.LoadWindowPlacement() == placement, "Window preferences bypassed the model adapter.");

    SettingsStore.FailSave = true;
    model.SetNumericSetting(SettingKey.ReductionPercent, 40);
    Check(model.Current.Settings.ReductionPercent == 40 && model.Current.Feedback.Contains("could not be saved"),
        "A save failure undid the live setting or hid the persistence error.");
    SettingsStore.FailSave = false;
    engine.FailVolume = true;
    await model.SetPlaybackVolumeAsync("spotify-session", 55);
    Check(model.Current.Feedback.Contains("synthetic session volume failure"),
        "An engine volume command failure did not reach model feedback.");
    engine.FailSettings = true;
    model.SetNumericSetting(SettingKey.ReductionPercent, 45);
    Check(model.Current.Feedback.Contains("synthetic audio settings failure"),
        "An asynchronous audio settings failure did not reach model feedback.");
    engine.FailSettings = false;
    StartupRegistration.Fail = true;
    await model.SetWindowsStartupAsync(true);
    Check(!model.Current.WindowsStartupBusy && model.Current.Feedback.Contains("synthetic startup"),
        "A failed startup registration left the model busy or hid its error.");
}
using (var relaunched = new VoiceDuckerModel(new Window()))
{
    await relaunched.InitializeAsync();
    Check(relaunched.Current.Audio.Running && !relaunched.Current.WindowsStartupEnabled,
        "A saved microphone checkbox edit did not enable capture on the next manual launch.");
}

FakeHost.Reset(new DuckingSettings { EnableMicrophoneOnWindowsStartup = true });
using (var model = new VoiceDuckerModel(new Window()))
{
    DuckingEngine.Last.FailInitialize = true;
    await model.InitializeAsync();
    Check(!model.Current.Audio.Ready && !model.Current.Audio.Running && !model.Current.Busy &&
        DuckingEngine.Last.MonitoringCalls == 0 && model.Current.Feedback.Contains("snapshot failure"),
        "Failed playback snapshot initialization allowed capture or hid the failure.");
    Check(!model.Current.WindowsStartupBusy,
        "Playback initialization failure left the independent Windows startup preference busy.");
    Check(model.Current.CanToggle,
        "A failed startup snapshot permanently disabled the retry command.");
    DuckingEngine.Last.FailInitialize = false;
    await model.ToggleMonitoringAsync();
    Check(model.Current.Audio.Ready && model.Current.Audio.Running && !model.Current.Busy,
        "Enable could not retry a failed playback snapshot before opening capture.");
}

foreach (var denied in new[] { LicenseState.Checking, LicenseState.Unavailable, LicenseState.Expired })
{
    FakeHost.Reset(new DuckingSettings { EnableMicrophoneOnWindowsStartup = true }, license: denied);
    using var model = new VoiceDuckerModel(new Window());
    await model.InitializeAsync();
    await model.ToggleMonitoringAsync();
    Check(!model.Current.Audio.Running && DuckingEngine.Last.MonitoringCalls == 0,
        $"The {denied} license state allowed microphone capture.");
    Check(!model.Current.CanToggle && model.Current.Feedback.Contains("license"),
        "License denial did not disable capture controls and report its reason.");
}

FakeHost.Reset(license: LicenseState.Full);
using (var model = new VoiceDuckerModel(new Window()))
{
    await model.InitializeAsync();
    await model.ToggleMonitoringAsync();
    StoreLicenseService.Last.SetState(LicenseState.Expired);
    Check(await Eventually(() => !model.Current.Audio.Running), "License loss while idle did not stop active capture.");
    Check(model.Current.TrialExpiredNoticePending, "Trial expiration was not exposed as application state.");
    model.DismissTrialExpiredNotice();
    StoreLicenseService.Last.SetState(LicenseState.Expired);
    Check(!model.Current.TrialExpiredNoticePending, "A dismissed expiration notice immediately reopened.");
}

FakeHost.Reset();
using (var model = new VoiceDuckerModel(new Window()))
{
    await model.InitializeAsync();
    var engine = DuckingEngine.Last;
    engine.MonitoringBlock = new(TaskCreationOptions.RunContinuationsAsynchronously);
    var firstToggle = model.ToggleMonitoringAsync();
    Check(model.Current.Busy, "An incomplete microphone operation was not marked busy.");
    var secondToggle = model.ToggleMonitoringAsync();
    Check(!secondToggle.IsCompleted && engine.MonitoringCalls == 1 && engine.MaximumConcurrentCommands == 1,
        "Overlapping toggles started competing microphone operations.");
    engine.MonitoringBlock.SetResult();
    await firstToggle;
    await secondToggle;
    Check(!model.Current.Busy && !model.Current.Audio.Running && engine.MonitoringCalls == 2,
        "A queued toggle was dropped or left stale busy/running state.");
    engine.MonitoringBlock = null;
    engine.FailMonitoring = true;
    await model.ToggleMonitoringAsync();
    Check(!model.Current.Busy && !model.Current.Audio.Running && model.Current.Feedback.Contains("synthetic microphone"),
        "A failed microphone start left capture active, busy, or without feedback.");
}

FakeHost.Reset(license: LicenseState.Full);
using (var model = new VoiceDuckerModel(new Window()))
{
    await model.InitializeAsync();
    await model.ToggleMonitoringAsync();
    var engine = DuckingEngine.Last;
    engine.SettingsBlock = new(TaskCreationOptions.RunContinuationsAsynchronously);
    var microphoneChange = model.SelectMicrophoneAsync("mic-b");
    Check(model.Current.Busy, "A pending microphone selection was not serialized.");
    StoreLicenseService.Last.SetState(LicenseState.Expired);
    engine.SettingsBlock.SetResult();
    await microphoneChange;
    Check(await Eventually(() => !model.Current.Audio.Running),
        "License loss during a pending microphone operation dropped the mandatory stop command.");
}

foreach (var automaticStart in new[] { false, true })
{
    FakeHost.Reset(new DuckingSettings { EnableMicrophoneOnWindowsStartup = automaticStart }, license: LicenseState.Full);
    using var model = new VoiceDuckerModel(new Window());
    var engine = DuckingEngine.Last;
    engine.BeforeMonitoringBlock = new(TaskCreationOptions.RunContinuationsAsynchronously);
    Task enabling;
    if (automaticStart) enabling = model.InitializeAsync();
    else
    {
        await model.InitializeAsync();
        enabling = model.ToggleMonitoringAsync();
    }
    Check(model.Current.Busy && !model.Current.Audio.Running,
        "The synthetic pre-start operation did not represent a pending capture command.");
    StoreLicenseService.Last.SetState(LicenseState.Expired);
    engine.BeforeMonitoringBlock.SetResult();
    await enabling;
    Check(await Eventually(() => !model.Current.Audio.Running),
        $"License loss before {(automaticStart ? "automatic" : "manual")} capture published Running=true left capture enabled.");
}

FakeHost.Reset();
var disposable = new VoiceDuckerModel(new Window());
await disposable.InitializeAsync();
var disposedEngine = DuckingEngine.Last;
var disposedLicense = StoreLicenseService.Last;
disposable.Dispose();
disposable.Dispose();
Check(disposedEngine.Disposed && disposedLicense.Disposed && disposedEngine.SubscriberCount == 0 &&
    disposedLicense.SubscriberCount == 0, "Disposal retained platform adapters or event subscriptions.");
var previous = disposable.Current;
disposedEngine.Publish(disposedEngine.State with { Running = true });
Check(disposable.Current == previous, "A disposed model still accepted audio callbacks.");

var projectionSettings = new DuckingSettings(50) { ThresholdDecibels = -30, HysteresisDecibels = 6 };
var detection = new GateState(DetectionPhase.AboveThreshold, true, 0.1, -20, -30, -36, 5);
var audio = new EngineStatus(true, true, true, 1, detection, "Fake active microphone",
    [new("spotify", "Spotify", 1, 0.72f, 0.36f, 0.36f, 0.01f, false, true, true, true),
     new("browser", "Browser", 2, 0.5f, 0.5f, 0.5f, 0.1f, true, false, false, true),
     new("self", "VoiceDucker.exe", 3, 1, 1, 1, 1, false, false, false, false)],
    [new("", "Fake default")]);
var view = new ApplicationState(projectionSettings, audio, false, false, false,
    new(false, true, "VoiceDucker", false, "", false, ""), "");
Check(view.PlaybackRows.Count == 2 && view.PlaybackRows[0].Name == "Spotify",
    "Playback projection did not filter the application itself or sort Spotify first.");
var row = view.PlaybackRows[0];
Check(Math.Abs(row.NormalPercent - 72) < 0.0001 && Math.Abs(row.ActualPercent - 36) < 0.0001,
    "Normal and actual playback volume were conflated in application state.");
Check(Math.Abs(row.SliderPercent - 36) < 0.0001 && row.SliderPercent == row.ActualPercent,
    "The playback cursor displayed the normal reference instead of the actual Windows volume.");
Check(row.VolumeText.Contains("Volume 36%") && row.VolumeText.Contains("Normal 72%"),
    "The playback row did not identify actual volume and its separate normal reference.");
Check(row.VolumeHelp.Contains("50%"),
    "The playback row did not explain the actual-volume ceiling during 50 percent ducking.");
Check((view with { Settings = projectionSettings with { ReductionPercent = 80 } }).PlaybackRows[0]
        .VolumeHelp.Contains("20%"),
    "The actual-volume help confused an 80 percent reduction with its 20 percent output ceiling.");
Check(Math.Abs(row.SignalPercent - 18.543416692242918) < 0.0001 && row.CanEdit,
    "Playback signal activity did not include the observed Windows session gain.");
Check(view.PlaybackRows[1].SignalPercent == 0 && !view.PlaybackRows[1].CanEdit &&
    view.PlaybackRows[1].VolumeText == "MUTED", "Muted playback was displayed as audible or editable.");
Check(view.VisualStateText == "AUDIO LOWERED" && view.MicrophoneLevelHint.Contains("Above threshold") &&
    Math.Abs(view.MicrophoneMeterFraction - 2d / 3) < 0.0001,
    "Application presentation did not use the engine's authoritative detector and affected-session count.");
Check(view.MicrophoneNameText.Contains("Fake active microphone"), "The active capture input was not projected.");
Check(view with { Settings = projectionSettings with { MicrophoneDeviceId = "disconnected" } } is { } missing &&
    missing.Microphones.Any(choice => choice.Id == "disconnected"),
    "A disconnected saved microphone disappeared from the object model.");
foreach (var (phase, expected) in new[]
{
    (DetectionPhase.Hysteresis, "hysteresis"), (DetectionPhase.Hold, "Waiting"),
    (DetectionPhase.Stale, "unavailable"), (DetectionPhase.Off, "Press Enable")
})
{
    var projected = view with { Audio = audio with { Detection = detection with { Phase = phase } } };
    Check(projected.MicrophoneLevelHint.Contains(expected), "A detector phase was reinterpreted by the view projection.");
}
Check(!(view with { Busy = true }).CanToggle && !(view with { Busy = true }).CanSelectMicrophone,
    "Busy state did not disable conflicting microphone commands.");

// Synthetic fixed source peak: the session meter is before volume, so the display
// must project actual output gain. Its 60 dB scale is logarithmic, not a volume bar.
PlaybackRowState OutputMeter(float actual, bool muted = false, float normal = 1)
{
    var stream = new PlaybackStream("meter", "Spotify", 101, normal, actual, actual,
        .4f, muted, true, actual < normal, true);
    return (view with { Audio = audio with { Streams = [stream] } }).PlaybackRows.Single();
}
var fullOutput = OutputMeter(1);
var halfOutput = OutputMeter(.5f);
var zeroOutput = OutputMeter(0);
Check(Math.Abs(fullOutput.SignalPercent - 86.73533362240125) < 0.0001,
    "Unity gain changed the synthetic source peak shown by the output meter.");
Check(Math.Abs(halfOutput.SignalPercent - 76.70100043360188) < 0.0001,
    "Half Windows gain did not attenuate the synthetic output meter by 6.0206 dB.");
Check(Math.Abs(fullOutput.SignalPercent - halfOutput.SignalPercent - 10.034333188799373) < 0.0001,
    "A 6.0206 dB reduction did not occupy the corresponding distance on the 60 dB meter scale.");
Check(fullOutput.NormalPercent == 100 && halfOutput.NormalPercent == 100 &&
    fullOutput.ActualPercent == 100 && halfOutput.ActualPercent == 50,
    "Output metering changed the separate normal/actual session volume projections.");
Check(zeroOutput.SignalPercent == 0 && zeroOutput.ActualPercent == 0,
    "A zero-volume session retained a visible output signal.");
Check(OutputMeter(1, muted: true).SignalPercent == 0,
    "Muted output retained the source meter's signal activity.");
Check(OutputMeter(.5f, normal: .2f).SignalPercent == halfOutput.SignalPercent,
    "Output metering used the requested normal volume instead of the observed Windows gain.");

var fullyLowered = view with
{
    Settings = projectionSettings with { ReductionPercent = 100 },
    Audio = audio with
    {
        Streams =
        [
            new("lowered", "Spotify", 1, .72f, 0, 0, .4f, false, true, true, true),
            new("other", "Browser", 2, .5f, .5f, .5f, .4f, false, false, false, false)
        ]
    }
};
Check(!fullyLowered.PlaybackRows.Single(stream => stream.Key == "lowered").CanEdit &&
    Math.Abs(fullyLowered.PlaybackRows.Single(stream => stream.Key == "lowered").NormalPercent - 72) < .0001,
    "An eligible zero-gain session remained editable or lost its normal reference.");
Check(fullyLowered.PlaybackRows.Single(stream => stream.Key == "other").CanEdit,
    "A 100 percent reduction disabled editing for a session outside automatic ducking.");
Check((fullyLowered with { Audio = fullyLowered.Audio with { Speaking = false } }).PlaybackRows
        .Single(stream => stream.Key == "lowered").CanEdit,
    "A 100 percent reduction disabled the volume cursor after detection released.");
Check((fullyLowered with { Settings = projectionSettings with { ReductionPercent = 99 } }).PlaybackRows
        .Single(stream => stream.Key == "lowered").CanEdit,
    "A nonzero automatic gain was treated as an uninvertible zero-gain session.");

FakeHost.Reset();
using (var model = new VoiceDuckerModel(new Window()))
{
    await model.InitializeAsync();
    var engine = DuckingEngine.Last;
    var commands = engine.PlaybackVolumeCalls;
    foreach (var actual in new[] { .8f, .6f, .5f, .7f, 1f })
    {
        var fading = new PlaybackStream("fading", "Spotify", 101, 1, actual, .5f,
            .4f, false, true, actual < 1, true);
        engine.Publish(audio with { Streams = [fading] });
        var fadingRow = model.Current.PlaybackRows.Single();
        Check(Math.Abs(fadingRow.SliderPercent - actual * 100) < .0001 && fadingRow.NormalPercent == 100,
            "A synthetic fade moved the normal reference or left the cursor at the pre-duck volume.");
        for (var render = 0; render < 5; render++) _ = model.Current.PlaybackRows.Single().SliderPercent;
    }
    Check(engine.PlaybackVolumeCalls == commands,
        "Reading playback rows to render a fade submitted a volume command.");
    foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, -1d, 101d })
    {
        await model.SetPlaybackVolumeAsync("fading", invalid);
        Check(engine.PlaybackVolumeCalls == commands && model.Current.Feedback.Length > 0,
            "An invalid actual playback volume reached the engine or was rejected without feedback.");
    }
}

if (failures.Count > 0)
    throw new InvalidOperationException($"Application model: {failures.Count} of {checks} checks failed:\n" +
        string.Join("\n", failures.Select(failure => "- " + failure)));
Console.WriteLine($"Application model: {checks} synthetic checks passed; no platform services or audio devices were accessed.");
'@ | Set-Content -LiteralPath (Join-Path $testRoot 'Program.cs') -Encoding utf8

dotnet build (Join-Path $testRoot 'ModelTests.csproj') --nologo --verbosity minimal `
    -p:ImportDirectoryBuildProps=false -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Synthetic application model harness did not build.' }
dotnet (Join-Path $testRoot 'bin\Debug\net10.0\ModelTests.dll')
if ($LASTEXITCODE -ne 0) { throw 'Synthetic application model checks failed.' }
