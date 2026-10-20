# VoiceDucker

**You talk. Music turns down. You stop. It comes back.**

I built VoiceDucker to talk to AI while Spotify keeps playing.
It can lower browser videos and other Windows apps too.

Useful for AI chats, Zoom or Teams calls, and demos or streams with
OBS Studio, Microsoft Clipchamp, or similar tools.

## Start here

1. On **Windows 11**, download a ZIP from [GitHub Releases](https://github.com/umbertotechnopreneur/VoiceDucker/releases).
   Choose **x64** for most PCs, or **ARM64** for Windows on ARM.
2. Extract the whole ZIP. Open `VoiceDucker.exe`. Keep the files together.
3. Play some music. Press **Enable**.
4. Speak. The music gets quieter. Pause, and it slowly comes back.

Press **Disable** to stop. The microphone always starts **off**.
The ZIP needs no installer; it is unsigned, so Windows may show a security prompt.

## Choose which apps get quieter

Spotify is included by default.

- Open **Settings > Apps & startup** and turn on **Also lower other apps** for browsers and other players.
- To leave an app alone, hover over its name and click the **X** (**Hide**).
- To bring it back, open **Hidden apps** and click its **X** (**Show**).

Hide your meeting app if you do not want its playback lowered.

## Need to change something?

Open **Settings**. Start with the defaults.

- Wrong mic? Choose it under **Microphone**.
- Not detecting your voice? Raise **Sensitivity**.
- Triggered by room noise? Raise **Detection threshold**.
- Music still too loud? Raise **Reduce volume by**.
- Music returns too soon? Raise **Wait before fade back**.

Leave the other settings alone until you need them.

Settings save automatically. The window remembers its size and position too.
MSIX updates keep these preferences. Uninstalling the MSIX removes its private
settings, so install a newer package over the existing one to keep them.

## Good to know

VoiceDucker reacts to microphone sound, not words. Opening a browser alone does
not lower audio. There are no recordings, transcription, or audio uploads.

For OBS or Clipchamp, make a short test recording first. VoiceDucker lowers
Windows playback; your recording's sound depends on its audio sources.

Opening VoiceDucker again shows the existing window, not a second copy.

At launch, VoiceDucker remembers each app audio session's volume in memory.
Every reduction and restoration uses that same baseline until VoiceDucker exits,
including after Disable/Enable or a mixer change. Sessions created later are
remembered when first detected. Muted and silent playback is left alone.
Windows volume and mute notifications are monitored even while the microphone
is off. VoiceDucker recognizes its own mixer writes separately from external
changes; external changes do not replace the startup baseline.

<img src="docs/screenshots/main-window.png" alt="VoiceDucker with the microphone off" width="480">

## For developers

[Build and debug](docs/DEVELOPMENT.md) · [Package and release](docs/RELEASING.md)

---

<p align="right">
  <a href="https://umbertogiacobbi.biz/vibeware/manifesto">
    <img src="https://raw.githubusercontent.com/umbertotechnopreneur/VibeWare/main/Branding/vibeware-logo.png" alt="VibeWare floppy logo" width="160">
  </a>
</p>

### This is VibeWare · [Manifesto](https://umbertogiacobbi.biz/vibeware/manifesto)

I am [Umberto Giacobbi](https://umbertogiacobbi.biz). I created VoiceDucker with
AI assistance. I guide the work and remain responsible for checking functionality,
performance, and security. VoiceDucker participates in the VibeWare initiative.

[VoiceDucker on GitHub](https://github.com/umbertotechnopreneur/VoiceDucker) · [VibeWare on GitHub](https://github.com/umbertotechnopreneur/VibeWare) · [umbertogiacobbi.biz](https://umbertogiacobbi.biz)

[MIT license](LICENSE) · [Terms](https://umbertogiacobbi.biz/terms/) · [Privacy](https://umbertogiacobbi.biz/privacy/)

## External binary security policy

Before including any third-party prebuilt binary or compiled library in this
repository or its deliverables, including files supplied without source code,
run an explicit targeted Microsoft Windows Defender scan of the downloaded
package/archive and all extracted files and required dependencies that will be
used. Keep them staged until the scan finishes; real-time protection alone is
not enough.

Proceed only after the scan completes without detections. Record the official
source, version or SHA-256, scan command and result. An unavailable, disabled,
failed or unfinished scan, or any detection, blocks import. Do not bypass a
detection with exclusions, allowlisting, restored quarantine items or disabled
protection without separate explicit owner approval.

This rule applies to future imports; it does not certify existing dependencies.
