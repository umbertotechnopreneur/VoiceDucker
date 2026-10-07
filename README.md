# VoiceDucker

I talk to AI while Spotify or other audio keeps playing in the background.
Windows' **Communications** setting did not lower Spotify the way I needed
during those conversations, so I built VoiceDucker.

I press **Enable** when I want it to listen to my microphone. When I speak,
VoiceDucker lowers Spotify; I can also choose to lower other playing apps.
When I stop, it brings them back to the volume I had chosen. It does not
change the Windows master volume or record my voice.

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

## Build

On Windows 11 with the .NET 10 SDK and Windows App SDK build tools, I run:

```powershell
.\scripts\build.ps1 -Architecture x64 -Configuration Debug -Run
```

My build puts the app at `artifacts/app/win-x64/VoiceDucker.exe`. I can build
ARM64 with `-Architecture ARM64`. I publish only portable x64 and ARM64 ZIPs
through GitHub Actions, never MSIX packages there.

For a local signed Release MSIX, I use a trusted code-signing certificate with
subject `CN=VibeWare` in `Cert:\CurrentUser\My`:

```powershell
pwsh -NoProfile -File .\scripts\package-msix.ps1 -Architecture x64 -CertificateThumbprint <thumbprint> -Install
```

## VibeWare

I am [Umberto Giacobbi](https://umbertogiacobbi.biz). I made VoiceDucker as
part of [VibeWare](https://umbertogiacobbi.biz/vibeware/manifesto), with AI
assistance and my own direction and review. I share it under the [MIT license](LICENSE).

<a href="https://umbertogiacobbi.biz/vibeware/manifesto"><img src="Assets/vibeware-logo.png" alt="VibeWare logo" width="180"></a>
