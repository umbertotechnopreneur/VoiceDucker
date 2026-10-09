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

<img src="docs/screenshots/main-window.png" alt="VoiceDucker with audio settings and active streams" width="480">

## My defaults

- Reduce volume by **90%**
- Fade down in **300 ms**
- Fade back to my previous volume in **30,000 ms**
- Start with Windows, with the microphone **off** until I press Enable
- Lower **Spotify** by default; other apps are optional

I can change these settings in the window. If I move an app's volume slider
myself, VoiceDucker leaves my new choice alone. On a normal exit, it returns
the sessions it still controls to their previous levels over **2,000 ms**.

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

---

<a href="https://umbertogiacobbi.biz/vibeware/manifesto">
  <img align="right" src="https://raw.githubusercontent.com/umbertotechnopreneur/VibeWare/main/Branding/vibeware-logo.png" alt="VibeWare floppy logo" width="180">
</a>

### This is VibeWare

VibeWare is a term coined by [Umberto Giacobbi](https://umbertogiacobbi.biz) and an open initiative for developers who build with AI and care about the craft. It challenges the assumption that vibe coding is synonymous with low-quality code. It openly acknowledges the weaknesses and risks of AI-generated software: experienced developers must guide the process, test carefully, check security and take responsibility for the result. A VibeWare footer simply says: this software was developed with AI, and it deserves to be judged by the quality of the work.

[Read the VibeWare manifesto](https://umbertogiacobbi.biz/vibeware/manifesto)

*You are welcome to explore, adapt and reuse the open-source VibeWare materials under the project license — my contribution to the developer community.*
