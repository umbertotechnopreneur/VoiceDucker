# Build and debug VoiceDucker

## Open it in Visual Studio

Use Windows 11, the .NET 10 SDK selected by `global.json`, and Visual Studio
with the **.NET desktop development** workload and Windows App SDK build tools.

1. Open `VoiceDucker.sln`.
2. Choose **Debug** and **x64**.
3. Select **VoiceDucker (Unpackaged)**.
4. Press **F5**.

This runs the app without installing it. Press **Enable** only when you want
microphone monitoring, or use the saved **Enable microphone on startup** opt-in
to start monitoring whenever the app opens. Close any running VoiceDucker first:
only one instance can run per Windows session. Choose **ARM64** to build for
Windows on ARM.

## Build from PowerShell

Run from the repository folder:

```powershell
pwsh -NoProfile -File scripts/build.ps1 -Architecture x64 -Configuration Debug -Run
```

The app is at `artifacts/app/win-x64/VoiceDucker.exe`. Omit `-Run` to build only.

## Find the code

- `MainWindow.xaml`: window layout.
- `MainWindow.xaml.cs`: window lifetime, tray and status display.
- `MainWindow.Settings.cs`: settings and microphone selection.
- `MainWindow.Playback.cs`: app rows, sliders and hidden apps.
- `MainWindow.Listening.cs`: profile and pause presentation and command forwarding.
- `Model/VoiceDuckerModel.cs`: owns application state, commands and persistence.
- `Model/VoiceDuckerModel.Listening.cs`: manual profile commands and runtime suspension.
- `Model/ApplicationState.cs`: immutable state and presentation projections.
- `Audio/ListeningProfile.cs`: saved audio preferences, excluding hardware, startup and live volumes.
- `Audio/DuckingEngine.cs`: one app-lifetime worker for audio and commands.
- `Audio/VoiceGate.cs`: decides when microphone sound should lower playback.
- `Audio/PlaybackSessions.cs`: lowers and restores app volumes.
- `Audio/PlaybackVolumeEvents.cs`: filters native callbacks by EventContext.
- `Audio/NativePlaybackSession.cs`: registers callbacks and writes tagged volumes.
- `Audio/NativePlaybackSource.cs`: worker-owned Core Audio discovery and readback.
- `SettingsStore.cs` and `WindowPlacementStore.cs`: save preferences and layout.
  Both use `AtomicJsonFile.cs` so an interrupted write does not replace saved data.

The model owns settings, startup, licensing and audio lifecycle; views only render
snapshots and submit typed commands. One audio worker retains session state across
Disable/Enable and microphone changes. Each session has immutable `Startup`, explicit
user-controlled `Normal`, fresh `Actual`, and a fade `Target`. Voice targets
`Normal * (1 - reduction / 100)`; silence targets `Normal`; Disable and exit restore
`Startup` for sessions changed by the app. Own fade readbacks never replace either
reference. A normal-volume slider command works during continuous detection,
without an interval bypass. External mixer payloads become explicit normal-volume
intent only when they match current native state, avoiding stale callback echoes.
Microphone
capture starts automatically on every app launch, including manual launches,
only when the saved **Enable microphone on startup** opt-in is enabled. It is
off by default and independent of **Start with Windows**. Always capture the
playback snapshot and complete normal license checks before enabling monitoring.
Changing the preference does not start capture immediately. Windows autostart
uses `StartupTask` for packaged launches and the Run key for portable launches.
Do not add old-version migration paths.

Manual profiles live in the existing atomic settings document. Applying a profile
submits a complete listening-settings snapshot; microphone identity, startup
preferences and session reference volumes are outside the profile. Profile edits
are explicit, and the model derives the selected profile's modified indication.

Temporary suspension lives only in `ApplicationState`. The model computes its
remaining seconds from a monotonic deadline on the existing audio observations,
so no UI timer owns the resume decision. Pause and resume share the serialized
lifecycle command path. Disable cancels the deadline; resume checks entitlement,
readiness and pending restoration before starting capture. All capture and mixer
operations stay on the existing audio worker.

Softer automatic fades use smoothstep interpolation from confirmed actual volume
to the target. A changed target, duration or curve restarts an unfinished fade at
the latest confirmed level. Safety restoration and explicit volume edits keep
their existing paths; startup and normal references are never derived from fades.

## Run the checks

```powershell
dotnet build VoiceDucker.sln -c Debug -p:Platform=x64
pwsh -NoProfile -File scripts/test-settings-persistence.ps1
pwsh -NoProfile -File scripts/test-microphone-pipeline.ps1
pwsh -NoProfile -File scripts/test-application-model.ps1
pwsh -NoProfile -File scripts/test-window-placement.ps1
pwsh -NoProfile -File scripts/test-single-instance.ps1
pwsh -NoProfile -File scripts/test-playback-restoration.ps1
```

Tests use isolated files, synthetic levels/displays and separate test processes.
They do not capture microphone audio. Passing them does not prove live audio or UI behavior.

The playback regression runs the production controller against an in-memory mixer
and clock, including continuous voice while editing normal volume, repeated and
interrupted fades, exact startup restoration, stable targets without writes,
mute/disappearance, failed writes/readbacks and foreign callback echoes.
The microphone harness uses synthetic PCM/float buffers and a fake clock for RMS,
gain, threshold, hysteresis, hold and stale input. The application-model harness
uses fake audio, settings, startup and licensing adapters; it checks opt-in,
operation ordering and license loss while capture is still starting.

On a Windows workstation with a playback endpoint, run the native callback check:

```powershell
pwsh -NoProfile -File scripts/test-volume-notifications.ps1
```

This also measures resource lifetime across 100 warmup and 1,000 native callback
subscribe/dispose cycles, reporting retained subscription objects, managed/private
memory and process handles. These are harness measurements, not full-app RAM.

This creates a silent session in the test process and changes only that session's
volume and mute state. It checks actual Windows callbacks, context filtering and
unregistration without opening the microphone or changing another app's audio.
To additionally verify the installed app, enable its diagnostics, open it with
microphone monitoring off, then supply `-InstalledAppDiagnosticsPath <log-path>`.
Add `-ShutdownInstalledAppProcessId <pid>` to invoke `test-app-lifetime.ps1` in
Windows PowerShell: it chooses **Close app** while external events are arriving,
checks cleanup diagnostics and confirms the entire app process exits. The test
notification generator and UI Automation helper also terminate before return.

## Local volume diagnostics

To diagnose a volume restoration problem, close the existing instance, set
`$env:VOICEDUCKER_AUDIO_LOG = '1'`, then run the Debug build from that shell.
Launches respect the saved **Enable microphone on startup** preference. To start
with capture off, clear that preference before closing the existing instance;
press **Enable** only when ready to reproduce the issue.
Remove the environment variable afterward with `Remove-Item Env:VOICEDUCKER_AUDIO_LOG`.
For packaged launches, an empty `audio-diagnostics.enabled` file beside
`settings.json` enables the same local log after restarting the app. Remove that
file and restart to disable logging.

Events are written to `%LOCALAPPDATA%\VibeWare\VoiceDucker\audio-diagnostics.log`
(under the package's redirected LocalCache when packaged). The file rotates at
1 MiB and keeps one previous file. Events contain session identifiers, process
names, volume values, fade transitions, external changes and restoration results.
No microphone samples are recorded and nothing is uploaded. Logging errors do
not interrupt audio. These diagnostics do not reconstruct an original volume
that an older process has already forgotten.

## Install a local MSIX

Use a trusted code-signing certificate with a private key and subject `CN=VibeWare`
in `Cert:\CurrentUser\My`. Replace `<thumbprint>` with its thumbprint:

```powershell
pwsh -NoProfile -File scripts/package-msix.ps1 -Architecture x64 -Configuration Debug -CertificateThumbprint <thumbprint> -Install
```

This builds Debug, stops the running app and installs the MSIX. Omit
`-Configuration Debug` to build Release. Local signing
is not Store signing. Public ZIPs and development MSIX packages use separate
version numbers. See [release instructions](RELEASING.md) before distributing either.
Announce the planned stop and restart before deployment. Preserve the saved
preferences and restart with microphone monitoring governed by **Enable microphone
on startup**, including when the restart is a manual launch.

For the local MSIX identity, audio preferences are stored at
`%LOCALAPPDATA%\Packages\VibeWare.VoiceDucker_af9ft7172qtwj\LocalCache\Local\VibeWare\VoiceDucker\settings.json`;
window layout uses `window-placement.json` in the same directory. Windows
redirects the application's `%LOCALAPPDATA%\VibeWare\VoiceDucker` writes into
that package container. An in-place update preserves it; uninstalling removes
it. Keep the package identity and publisher unchanged and increment the MSIX
version for local updates. Back up these JSON files outside the container before
deployment. Do not uninstall as part of an update.

The worker-owned playback source registers
[`IAudioSessionEvents.OnSimpleVolumeChanged`](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessionevents-onsimplevolumechanged)
on each session using `RegisterAudioSessionNotification`, including inactive
sessions and while microphone monitoring is disabled. All fades, restoration and
normal-volume commands pass a per-process `EventContext` to `SetMasterVolume`. Callbacks
with that context are ignored. External volume/mute callbacks publish scalar
state to a concurrent store; no COM callback writes a log, touches UI, changes
volume or waits for another thread. Slider commands enter the worker queue and
change normal volume. There is no direct UI mixer path or manual interval bypass.
Volume setters skip unchanged values; completed fades acknowledge native readback
rather than repeatedly writing a quantized target. A target already equal to the
actual volume completes immediately. Status reflects observed attenuation, not
session ownership or a pending fade.

Capture callbacks publish bounded scalar RMS observations with timestamp,
sequence and capture generation; audio samples are not retained. The worker
applies sensitivity, threshold, hysteresis and hold using one settings snapshot.
Polling or changing a slider cannot renew the observation timestamp. Missing
fresh input for one second stops monitoring and requests safety restoration.
The model publishes the authoritative gate phase and level for presentation.

NAudio 2.2.1's convenience callback omits EventContext and its volume setter uses
an empty context, so a small native bridge owns the underlying session interfaces.
The worker releases subscriptions for disappeared or expired sessions while
retaining scalar normal/startup values and any unresolved restoration ownership.
Closing stops model timers, deactivates and unregisters callbacks, releases COM
references and stops the audio worker.
Cleanup continues across individual disposal failures and logs them. The app
explicitly exits its message loop after closing, including when worker shutdown
times out; a timeout never certifies that playback was restored. There is no
separate callback process. These events describe Windows session volume; an
app's internal DSP gain may not change that volume.
