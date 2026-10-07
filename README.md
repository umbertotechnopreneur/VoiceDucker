# VoiceDucker

I use AI voice chats a lot, and I got tired of turning down music and other
audio every time I spoke. I wanted one tiny switch: while my microphone picks
up my voice, lower whatever is playing; when I stop, bring it back. I put the
first version together in about fifteen minutes. It is a small personal tool,
and I offer it as is, without guarantees.

I am **Umberto Giacobbi**, the creator of VoiceDucker. This is my MIT-licensed
repository and part of the [VibeWare initiative](https://umbertogiacobbi.biz/vibeware/manifesto).

VoiceDucker watches the default Windows communications microphone **only after
I press Enable**. When its signal crosses a threshold, the app halves the
volume of audio sessions currently playing on active output devices. It
restores levels it still owns after a short quiet period. It does not change
the system master volume. If I move an app's slider in the Windows volume
mixer, VoiceDucker leaves my new level alone.

This is a signal-level gate, not speech recognition: a loud room or speakers
feeding into the microphone can trigger it. Headphones help. Microphone data
stays in memory; the app does not record, transmit, or save it. Closing the
window stops capture and attempts to restore changed sessions. If the process
is killed or an audio device disappears, I may need to reset a level manually
in the Windows volume mixer.

## Build and try it

On Windows 11, with the .NET 10 SDK and Windows App SDK build tools:

```powershell
dotnet restore VoiceDucker.csproj --locked-mode -p:Platform=x64 -r win-x64
dotnet build VoiceDucker.csproj -c Debug -p:Platform=x64 -r win-x64 --no-restore
dotnet run --project VoiceDucker.csproj -c Debug -p:Platform=x64 -r win-x64 --no-restore
```

Allow microphone access if Windows asks. Start some audio, press **Enable**,
speak, then press **Disable** to stop. The threshold is fixed for now, so I do
not promise it will suit every microphone or room. There is no account,
telemetry, network service, startup task, or audio recording.

The code uses WinUI 3 and NAudio's Windows Core Audio sessions. I directed the
implementation with OpenAI Codex; the source headers and Git history record
that provenance. The logo in `Assets/vibeware-logo.png` is from the VibeWare
brand repository. VoiceDucker is under the [MIT license](LICENSE).

---

<a href="https://umbertogiacobbi.biz/vibeware/manifesto">
  <img align="right" src="https://raw.githubusercontent.com/umbertotechnopreneur/VibeWare/main/Branding/vibeware-logo.png" alt="VibeWare floppy logo" width="180">
</a>

### This is VibeWare

VibeWare is a term coined by [Umberto Giacobbi](https://umbertogiacobbi.biz) and an open initiative for developers who build with AI and care about the craft. It challenges the assumption that vibe coding is synonymous with low-quality code. It openly acknowledges the weaknesses and risks of AI-generated software: experienced developers must guide the process, test carefully, check security and take responsibility for the result. A VibeWare footer simply says: this software was developed with AI, and it deserves to be judged by the quality of the work.

[Read the VibeWare manifesto](https://umbertogiacobbi.biz/vibeware/manifesto)

*You are welcome to explore, adapt and reuse the open-source VibeWare materials under the project license — my contribution to the developer community.*
