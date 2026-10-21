# Build and debug VoiceDucker

## Open it in Visual Studio

Use Windows 11, the .NET 10 SDK selected by `global.json`, and Visual Studio
with the **.NET desktop development** workload and Windows App SDK build tools.

1. Open `VoiceDucker.sln`.
2. Choose **Debug** and **x64**.
3. Select **VoiceDucker (Unpackaged)**.
4. Press **F5**.

This runs the app without installing it. Press **Enable** only when you want
microphone monitoring. Close any running VoiceDucker first: only one instance
can run per Windows session. Choose **ARM64** to build for Windows on ARM.

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
- `Audio/DuckingEngine.cs`: starts capture and coordinates volume changes.
- `Audio/VoiceGate.cs`: decides when microphone sound should lower playback.
- `Audio/PlaybackSessions.cs`: lowers and restores app volumes.
- `Audio/PlaybackVolumeEvents.cs`: filters native callbacks by EventContext.
- `Audio/NativePlaybackSession.cs`: registers callbacks and writes tagged volumes.
- `SettingsStore.cs` and `WindowPlacementStore.cs`: save preferences and layout.
  Both use `AtomicJsonFile.cs` so an interrupted write does not replace saved data.

Keep changes small. Playback baselines are captured at launch and kept in
`PlaybackVolumeSnapshot` for the whole application lifetime. UI monitoring adds
new sessions even while the microphone is off. Disable/Enable and microphone
changes must reuse the same snapshot. External mixer edits never replace it:
the next reduction/restoration still targets the captured level. Microphone
capture must stay off on manual launches. Windows startup may enable capture only
with the separate saved microphone opt-in, after the playback snapshot and normal
license checks. Packaged launches use `StartupTask` activation; portable Run entries
pass `--windows-startup`. Do not add old-version migration paths.

## Run the checks

```powershell
dotnet build VoiceDucker.sln -c Debug -p:Platform=x64
pwsh -NoProfile -File scripts/test-settings-persistence.ps1
pwsh -NoProfile -File scripts/test-window-placement.ps1
pwsh -NoProfile -File scripts/test-single-instance.ps1
pwsh -NoProfile -File scripts/test-playback-restoration.ps1
```

Tests use isolated files, synthetic levels/displays and separate test processes.
They do not capture microphone audio. Passing them does not prove live audio or UI behavior.

The playback regression runs the production session loop against a fake mixer and
clock. It checks repeated and interrupted fades, exact restoration, immutable
startup snapshots, worker restarts, sessions appearing later, external mixer
edits, muted/silent sessions, and immediate restoration on Disable.
It also checks EventContext filtering, external volume/mute callbacks, inactive
sinks, independent sessions, and external changes smaller than the old polling
tolerance.
It also drives the real voice gate and session loop together through repeated
voice/noise cycles, including new voice during an unfinished fade back.

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
Capture still starts off; press **Enable** only when ready to reproduce the issue.
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

For the local MSIX identity, audio preferences are stored at
`%LOCALAPPDATA%\Packages\VibeWare.VoiceDucker_af9ft7172qtwj\LocalCache\Local\VibeWare\VoiceDucker\settings.json`;
window layout uses `window-placement.json` in the same directory. Windows
redirects the application's `%LOCALAPPDATA%\VibeWare\VoiceDucker` writes into
that package container. An in-place update preserves it; uninstalling removes
it. Keep the package identity and publisher unchanged and increment the MSIX
version for local updates. Back up these JSON files outside the container before
deployment. Do not uninstall as part of an update.

The playback monitor registers
[`IAudioSessionEvents.OnSimpleVolumeChanged`](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessionevents-onsimplevolumechanged)
on each session using `RegisterAudioSessionNotification`, including inactive
sessions and while microphone monitoring is disabled. All fades, restoration and
UI-slider writes pass a per-process `EventContext` to `SetMasterVolume`. Callbacks
with that context are ignored. External volume/mute callbacks publish scalar
state to a concurrent store; no COM callback writes a log, touches UI, changes
volume or waits for another thread. The worker restarts an affected fade from
the current mixer level while retaining the immutable startup baseline.

NAudio 2.2.1's convenience callback omits EventContext and its volume setter uses
an empty context, so a small native bridge owns the underlying session interfaces.
The monitor logs external changes on its UI tick and releases subscriptions for
disappeared or expired sessions. The audio worker also releases expired-session
handles rather than keeping them until Disable. Closing stops and detaches the timer, deactivates and
unregisters every callback, releases COM references and stops the audio worker.
Cleanup continues across individual disposal failures and logs them. The app
explicitly exits its message loop after closing, including when worker shutdown
times out; a timeout never certifies that playback was restored. There is no
separate callback process. These events describe Windows session volume; an
app's internal DSP gain may not change that volume.
