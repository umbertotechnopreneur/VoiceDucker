# VoiceDucker

## Why I built it

Imagine your music is playing and you want to talk. You say, "A little quieter, please!" The music turns down, lets you finish, then comes back. That is the whole idea. No shouting match with the speakers.

I talk to AI while Spotify keeps me company. Windows' **Communications** setting did not lower Spotify the way I needed, and reaching for the volume slider every time got old. So I made a tiny volume referee: VoiceDucker.

I press **Enable** when I want it to listen to my microphone. When I speak, it lowers Spotify; I can choose other playing apps too. When I stop, it fades them back to the volume I chose. My voice stays in memory: no recordings, transcription, or uploads.

Created by [Umberto Giacobbi](https://umbertogiacobbi.biz), shared under the [MIT license](LICENSE).

## Download and play

The first public version is **0.1.0**, tagged **v0.1.0-stable**. Get the matching ZIP from [GitHub Releases](https://github.com/umbertotechnopreneur/VoiceDucker/releases):

- **Windows x64:** `VoiceDucker-win-x64.zip`, for most Intel and AMD PCs.
- **Windows ARM64:** `VoiceDucker-win-arm64.zip`, for Windows on ARM.

On **Windows 11**, extract the whole ZIP into a folder you can write to, then open `VoiceDucker.exe`. Keep its companion files together; the ZIP includes the .NET and Windows App SDK runtimes. The microphone starts **off**. Put some music on, press **Enable**, and talk at your usual volume. Adjust the threshold if it needs a nudge.

The ZIP is portable and unsigned. Windows may show a security prompt. There is no MSIX installation or signing certificate to import for this download.

## Screenshot

<img src="docs/screenshots/main-window.png" alt="VoiceDucker with the microphone off and advanced options collapsed" width="480">

## My defaults

- Reduce volume by **90%**
- Fade down in **300 ms**
- Fade back to my previous volume in **30,000 ms**
- Start with Windows, with the microphone **off** until I press Enable
- Lower **Spotify** by default; other apps are optional

Every valid audio setting is saved immediately, including while I type a number,
and restored after app or Windows restarts. Windows startup changes take effect
as soon as I toggle the checkbox. The microphone still starts off.

Each app appears as a simple row without a card or Remove button. Its live
session peak meter overlays the volume slider, with a -60 to 0 dBFS display range.
The first Advanced option is **Sensitivity**, a full-width slider from -24 to +36 dB.
It applies software gain after microphone capture, before sound detection and the
microphone meter. It updates immediately and is saved across restarts. It does
not change the Windows microphone volume or the input used by other apps.
Any sound above the resulting detection threshold can lower playback.

**Reset** next to Sensitivity returns microphone gain to 0 dB. The microphone
level meter uses a thick green bar, shows the live post-gain signal from -60 to 0 dBFS, and marks the detection
threshold at about -35 dBFS, so adjusting sensitivity has a visible reference.
The meter labels whether the current sound is above or below the threshold.
While the microphone is off, it prompts me to press Enable to see the level.
The Sensitivity slider uses the same green color.

**Microphone** lets me select an active input or use the Windows communications
default; the main window shows the actual device in use. Selecting another input
while enabled restarts capture on that device. If a saved device is unavailable,
VoiceDucker reports it rather than silently switching microphones. The list
refreshes when I open the selector. Selecting an input while disabled does not
start capture.

**Wait before fade back (ms)** keeps audio lowered during short pauses, starting
from the last detected sound. The default is 650 ms, adjustable from 0 to 10,000 ms.
After that wait, the existing Fade back duration controls the return to my volume.
Changing the wait takes effect immediately. Both the microphone choice and wait
are saved across restarts.

I can change these settings in the window. If I move an app's volume slider
myself, VoiceDucker leaves my new choice alone. On a normal exit, it returns
the sessions it still controls to their previous levels over **2,000 ms**.

VoiceDucker's own audio sessions are filtered by process name. Existing hidden
apps remain excluded; **Hidden apps** in Advanced options lets me restore them.

See [release and WinGet instructions](docs/RELEASING.md) for packaging and publication.

## Build

On Windows 11 with the .NET 10 SDK and Windows App SDK build tools, I run:

```powershell
.\scripts\build.ps1 -Architecture x64 -Configuration Debug -Run
```

The build puts the app at `artifacts/app/win-x64/VoiceDucker.exe`. Build
ARM64 with `-Architecture ARM64`. I publish only portable x64 and ARM64 ZIPs
through GitHub Actions, never MSIX packages there.

For a local signed Release MSIX, I use a trusted code-signing certificate with
subject `CN=VibeWare` in `Cert:\CurrentUser\My`:

```powershell
pwsh -NoProfile -File .\scripts\package-msix.ps1 -Architecture x64 -CertificateThumbprint <thumbprint> -Install
```

The installed MSIX keeps the Start menu identity
`VibeWare.VoiceDucker_af9ft7172qtwj!App` across updates and registers the
`VoiceDucker.exe` execution alias. Use the installed Start menu entry or this
alias to launch the installed app. The portable build uses the stable path
`artifacts/app/win-x64/VoiceDucker.exe`; temporary validation builds stay under
`artifacts` and do not create launch shortcuts.

---

<p align="right">
  <a href="https://umbertogiacobbi.biz/vibeware/manifesto">
    <img src="https://raw.githubusercontent.com/umbertotechnopreneur/VibeWare/main/Branding/vibeware-logo.png" alt="VibeWare floppy logo" width="160">
  </a><br>
  <strong>VibeWare</strong><br>
  <em>Human intent, AI execution, and plenty of tokens</em>
</p>

### What is VibeWare?

I coined the term [VibeWare](https://umbertogiacobbi.biz) and started an open initiative for developers who build with AI and care about the craft. I challenge the assumption that vibe coding is synonymous with low-quality code, while openly acknowledging the weaknesses and risks of AI-generated software: experienced developers must guide the process, test carefully, check security and take responsibility for the result. With this footer, I acknowledge that I developed this software with AI and ask you to judge the quality of the work.

[**Read the VibeWare manifesto**](https://umbertogiacobbi.biz/vibeware/manifesto)

*You are welcome to explore, adapt and reuse the open-source VibeWare materials under the project license — my contribution to the developer community.*
