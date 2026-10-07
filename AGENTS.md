# VoiceDucker repository instructions

## Purpose

This is VibeWare's focused Windows utility for lowering Spotify desktop audio
while the user's communications microphone is above a signal threshold. Keep it
small and understandable. Do not add speech transcription, recording, telemetry,
network calls, startup registration, or other app targets without a request.

## Platform and ownership

- Use WinUI 3 and the Windows App SDK; do not replace the UI with Windows Forms.
- The audio engine owns microphone capture and Spotify Core Audio sessions.
  Never adjust system master volume or audio sessions from unrelated processes.
- Preserve user mixer changes. Restore only a level the app still owns and
  report failures instead of claiming Spotify was restored.
- Keep microphone capture opt-in, in memory only, and inactive at launch.
- Preserve the approved VibeWare logo's pixels and transparency. Follow its
  lo-fi 1980s computing style without neon effects.

## Workflow

- Read `README.md` and inspect Git status before editing.
- Keep generated outputs and logs out of Git. Pin package versions.
- Build after code edits and state separately whether an interactive microphone
  and Spotify test was actually performed.
- Work on topic branches and propose pull requests for `main`. The repository
  owner controls merges and any direct bypass allowed by GitHub protection.
- Keep public documentation in English; discuss work with the owner in Italian.
