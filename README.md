# VoiceDucker

**Keep your soundtrack. Give your voice the spotlight.**

VoiceDucker lowers Spotify and, optionally, other playing Windows apps when you
speak, then gently brings them back. Press **Enable**, talk naturally, and let
background audio make room. No shouting match with the speakers. No constant
trips to the volume slider.

## Make room for your voice

- **Talk to AI. Keep the music.** Chat naturally with an AI assistant while music
  or videos play in the background. When you speak, playback steps back; when
  you're done, it returns.
- **Keep browser audio in its place.** A tab starts playing while you're talking?
  Include other apps so browser playback can step back too, instead of drowning
  you out.
- **Your demo, not your playlist.** Use VoiceDucker alongside **OBS Studio**,
  **Microsoft Clipchamp**, or other streaming and recording tools to keep
  unrelated Windows playback lower while you narrate a demo, tutorial, or stream.
- **Take the call, not the volume controls.** Keep background music from
  competing with you during **Zoom**, **Microsoft Teams**, and similar meetings.

Spotify is included by default. Turn on **Also lower other apps** for browsers
and other players. Hide apps you want to leave untouched, such as your meeting
app. The trigger is microphone sound: opening a browser or joining a call alone
does not lower audio.

For recording and streaming, VoiceDucker controls Windows playback volume, not
the recording software's internal mixer or audio tracks. What reaches the
recording or audience depends on the capture source and audio routing. Check a
short recording first; [OBS audio sources](https://obsproject.com/kb/application-audio-capture-guide)
and [Clipchamp screen recording](https://support.microsoft.com/en-us/clipchamp/how-to-make-a-screen-recording)
offer different capture options.

## Why I built it

I talk to AI while Spotify keeps me company. Windows' **Communications** setting did not lower Spotify the way I needed, and reaching for the volume slider every time got old. So I made a tiny volume referee: VoiceDucker.

I press **Enable** when I want it to listen to my microphone. My voice stays in memory: no recordings, transcription, or uploads.

Created by [Umberto Giacobbi](https://umbertogiacobbi.biz), shared under the [MIT license](LICENSE).

## Download and play

The first public version is **0.1.0**, tagged **v0.1.0-stable**. Get the matching ZIP from [GitHub Releases](https://github.com/umbertotechnopreneur/VoiceDucker/releases):

- **Windows x64:** `VoiceDucker-win-x64.zip`, for most Intel and AMD PCs.
- **Windows ARM64:** `VoiceDucker-win-arm64.zip`, for Windows on ARM.

On **Windows 11**, extract the whole ZIP into a folder you can write to, then open `VoiceDucker.exe`. Keep its companion files together; the ZIP includes the .NET and Windows App SDK runtimes. The microphone starts **off**. Put some music on, press **Enable**, and talk at your usual volume. Adjust the threshold if it needs a nudge.

If monitoring is still off 15 seconds after I open the app, Windows shows a one-time reminder: “VoiceDucker is not monitoring your microphone. Press Enable when you're ready.” It never turns the microphone on for me.

Only one VoiceDucker instance runs per Windows session. Opening it again brings
the existing window forward, including from the system tray, and shows
“VoiceDucker is already running”. It keeps the current monitoring state and
window layout instead of starting another audio engine.

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

The main window also remembers its position, size, monitor, DPI scale, maximized
state, and whether Settings was open. Reopening the app keeps that layout instead
of centering or resizing it again when audio streams change. If I disconnect the
saved monitor, the window reopens inside the primary monitor's work area. If I
change Windows scaling, it keeps its logical size at the monitor's current DPI.
Window placement is saved separately from audio settings, including when I hide
the app in the tray.

Each app appears as a simple row without a card. Hover over its name to reveal
an **X** with the **Hide** tooltip; keyboard focus reveals it too. Hiding an app
excludes it from the list and automatic volume lowering. **Hidden apps** in
Apps & startup opens a scrollable list with an X per app (**Show**) to bring it
back. The panel stays open while I restore apps and shows **No hidden apps.**
when empty. These choices are saved immediately.

Each row's live session peak meter overlays the volume slider, with a -60 to 0 dBFS display range.
The **Settings** cog at the top opens a sliding pane on the right, widening the
window while keeping the main view compact. Settings scroll independently.
Click the cog again to close the pane and return to the narrow window.
At the top of Settings, **Visit our online tutorial and parameter guide** opens
the [online guide](https://umbertogiacobbi.biz/voiceducker/guide/) in my browser.
The link includes static campaign tags (`utm_source=voiceducker`,
`utm_medium=referral`, `utm_campaign=in_app_help`,
`utm_content=settings_parameters`) to identify visits from this help link.
On smaller displays the width is limited to the available screen space.
Settings are grouped in three compact accordion panels: **Microphone & detection**,
**Volume & timing**, and **Apps & startup**. Opening one closes the previous panel;
all panels can be collapsed to keep the window compact.

The first microphone option is **Sensitivity**, a full-width slider from -24 to +36 dB.
It applies software gain after microphone capture, before sound detection and the
microphone meter. It updates immediately and is saved across restarts. It does
not change the Windows microphone volume or the input used by other apps.
Any sound above the resulting detection threshold can lower playback.

**Reset** next to Sensitivity returns microphone gain to 0 dB. The microphone
level meter uses a thick green bar, shows the live post-gain signal from -60 to 0 dBFS, and marks the detection
threshold, so adjusting sensitivity has a visible reference.
The meter labels whether the current sound is above or below the threshold.
While the microphone is off, it prompts me to press Enable to see the level.
The Sensitivity slider uses the same green color.

**Detection threshold**, below Sensitivity in Microphone & detection, adjusts the trigger
from -60 to 0 dBFS in 0.1 dB steps. Lower values detect quieter sounds; higher
values require louder sounds. It applies to the signal after Sensitivity gain.
The meter marker and threshold label follow the selected value. Changes take
effect while listening and are saved immediately across restarts. **Reset** restores
the default threshold (about -34.9 dBFS).

**Hysteresis (dB)** separates the trigger and release thresholds. With the default
6 dB gap, a -35 dBFS trigger stays active through dips down to -41 dBFS. Only
sound below the release threshold starts the pause before fade back. This avoids
repeated transitions when the microphone level hovers around the trigger. The
gap is adjustable from 0 to 12 dB and saved immediately; 0 restores a single
threshold. The release threshold is shown below the control.

**Microphone** lets me select an active input or use the Windows communications
default; the main window shows the actual device in use. Selecting another input
while enabled restarts capture on that device. If a saved device is unavailable,
VoiceDucker reports it rather than silently switching microphones. The list
refreshes when I open the selector. Selecting an input while disabled does not
start capture.

**Wait before fade back (ms)** keeps audio lowered during short pauses, starting
from the last sound sustained by the detector, including its hysteresis band.
The default is 650 ms, adjustable from 0 to 10,000 ms.
After that wait, the existing Fade back duration controls the return to my volume.
Changing the wait takes effect immediately. Both the microphone choice and wait
are saved across restarts.

I can change these settings in the window. If I move an app's volume slider
myself, VoiceDucker leaves my new choice alone. On a normal exit, it returns
the sessions it still controls to their previous levels over **2,000 ms**.

VoiceDucker's own audio sessions are filtered by process name. Existing hidden
apps remain excluded; **Hidden apps** in Apps & startup lets me restore them.

See [release and WinGet instructions](docs/RELEASING.md) for packaging and publication.

## Build

Open `VoiceDucker.sln` in Visual Studio with the .NET desktop development
workload and Windows App SDK build tools installed. The solution contains one
WinUI project and only the supported architectures: x64 and ARM64.

For ordinary local debugging, choose **Debug** and **x64**, select the
**VoiceDucker (Unpackaged)** launch profile, then press **F5**. It builds and
starts the app directly for debugging; it does not install an MSIX and the
microphone remains off until I press **Enable**. The packaged profile stays
available when I specifically want to debug package identity or MSIX behavior.

From PowerShell on Windows 11 with the .NET 10 SDK and Windows App SDK build
tools, I can produce the same portable Debug build with:

```powershell
.\scripts\build.ps1 -Architecture x64 -Configuration Debug -Run
```

The build puts the app at `artifacts/app/win-x64/VoiceDucker.exe`. Build
ARM64 with `-Architecture ARM64`. I publish only portable x64 and ARM64 ZIPs
through GitHub Actions, never MSIX packages there.

For a local signed Release MSIX installation, I use a trusted code-signing
certificate with subject `CN=VibeWare` in `Cert:\CurrentUser\My`:

```powershell
pwsh -NoProfile -File .\scripts\package-msix.ps1 -Architecture x64 -CertificateThumbprint <thumbprint> -Install
```

The installed MSIX keeps the Start menu identity
`VibeWare.VoiceDucker_af9ft7172qtwj!App` across updates and registers the
`VoiceDucker.exe` execution alias. Use the installed Start menu entry or this
alias to launch the installed app. The portable build uses the stable path
`artifacts/app/win-x64/VoiceDucker.exe`; temporary validation builds stay under
`artifacts` and do not create launch shortcuts.

The portable release version is `0.1.0`; the package manifest has its separate
MSIX development/build number. See [release and WinGet instructions](docs/RELEASING.md)
before installing one over the other.

---

<p align="right">
  <a href="https://umbertogiacobbi.biz/vibeware/manifesto">
    <img src="https://raw.githubusercontent.com/umbertotechnopreneur/VibeWare/main/Branding/vibeware-logo.png" alt="VibeWare floppy logo" width="160">
  </a><br>
  <strong>VibeWare</strong><br>
  <em>Human intent, AI execution, and plenty of tokens</em>
</p>

### This is VibeWare

This project participates in the **VibeWare initiative**, an open movement for developers who build useful software with AI under human direction. VibeWare describes participation in the initiative; it is not the application's author or publisher.

I am [Umberto Giacobbi](https://umbertogiacobbi.biz). I define this project's purpose, guide its implementation, and remain responsible for checking the result. Each project keeps its own identity, license, release status and validation evidence.

[**Read the VibeWare manifesto**](https://umbertogiacobbi.biz/vibeware/manifesto)

[VoiceDucker on GitHub](https://github.com/umbertotechnopreneur/VoiceDucker) · [VibeWare on GitHub](https://github.com/umbertotechnopreneur/VibeWare) · [umbertogiacobbi.biz](https://umbertogiacobbi.biz)

*Explore, adapt and reuse the project materials under their applicable licenses.*

[Terms](https://umbertogiacobbi.biz/terms/) · [Privacy](https://umbertogiacobbi.biz/privacy/)
