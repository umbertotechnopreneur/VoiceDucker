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
- `SettingsStore.cs` and `WindowPlacementStore.cs`: save preferences and layout.
  Both use `AtomicJsonFile.cs` so an interrupted write does not replace saved data.

Keep changes small. Never undo a user's manual mixer change. Microphone capture
must stay off at startup. Do not add old-version migration paths.

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
clock. It checks repeated and interrupted fades, exact restoration, manual mixer
edits, muted/silent sessions, and immediate restoration on Disable.
It also drives the real voice gate and session loop together through repeated
voice/noise cycles, including new voice during an unfinished fade back.

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
names, volume values, fade transitions, lost ownership and restoration results.
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
