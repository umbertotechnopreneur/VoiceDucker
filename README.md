# VoiceDucker

VoiceDucker is a small Windows 11 app by **VibeWare**. It listens to the default
Windows communications microphone while enabled and halves the volume of active
**Spotify desktop** audio sessions when microphone level crosses a fixed gate.
After a short pause, it restores each session's earlier volume. It never changes
the system master volume, Spotify's own settings, or other applications.

The app uses WinUI 3 and a desktop Acrylic backdrop. Its only main control is
**Enable / Disable**. It starts disabled and stops listening when closed.

## What it does and does not detect

VoiceDucker uses microphone signal level, not speech recognition. Loud background
sound can trigger it, especially if speakers feed back into the microphone.
Headphones make the intended behavior more reliable. The fixed threshold is an
initial value, not a calibrated voice activity detector. Captured microphone
buffers are processed in memory and are not retained, sent over a network, or
written to disk.

Spotify must be running as the Windows desktop application; a Spotify browser tab
is outside this first version. The app discovers active playback sessions, so
Spotify may need to begin playback before the status changes from “not found.”
If you change Spotify's mixer level while it is lowered, VoiceDucker leaves your
new level alone instead of overwriting it during restoration.

## Build and run

Requirements: Windows 11, .NET 10 SDK, Windows App SDK build tools, and an active
microphone. The app is a packaged WinUI 3 project created from Microsoft's
template. Package signing and installation are not part of this source release.

```powershell
dotnet restore VoiceDucker.csproj --locked-mode -p:Platform=x64 -r win-x64
dotnet build VoiceDucker.csproj -c Debug -p:Platform=x64 -r win-x64 --no-restore
dotnet run --project VoiceDucker.csproj -c Debug -p:Platform=x64 -r win-x64
```

Windows must allow microphone access for VoiceDucker. Disable it before
changing audio devices. Closing the window also attempts to restore Spotify's
earlier mixer level; an abrupt process termination or device failure can prevent
that restoration, in which case use the Windows volume mixer.

## Development and evidence

The app is intentionally limited to one window, one microphone capture stream,
and Spotify's own Core Audio sessions. There is no tray service, startup task,
account, analytics, network API, or automatic update mechanism.

Local verification: x64 Debug compiled with no warnings, and Release generated
an unsigned MSIX. The Release packaging tool warned that the optional symbol
packaging executable was unavailable; the MSIX build itself succeeded. No app
installation or interactive microphone/Spotify test has been performed.
Microphone permissions, Spotify session discovery, perceived ducking, audio
device changes, and actual memory use remain unverified.

The initial implementation was prepared with Codex under Umberto Giacobbi's
direction. Its local build is reported separately from interactive audio checks.

The unmodified VibeWare logo in `Assets/vibeware-logo.png` comes from the VibeWare
brand repository. VibeWare was created by [Umberto Giacobbi](https://umbertogiacobbi.biz).
The project's MIT license is in [LICENSE](LICENSE).
