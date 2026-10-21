# VoiceDucker repository instructions

## Purpose

This is Umberto Giacobbi's MIT-licensed utility, part of the VibeWare initiative,
for lowering playing audio sessions
while the user's communications microphone is above a signal threshold. Keep it
small and understandable. Do not add speech transcription, recording, telemetry,
network calls or startup registration without a request.

## Platform and ownership

- Use WinUI 3 and the Windows App SDK; do not replace the UI with Windows Forms.
- The audio engine owns microphone capture and active playback Core Audio sessions.
  Never adjust system master volume or muted or silent sessions.
- Capture playback-session volume baselines when the app opens and keep them
  unchanged until it exits, including across Disable/Enable and external mixer
  changes. Capture new sessions when first observed. Restore changed sessions
  to those baselines and report failures instead of claiming audio was restored.
- Keep microphone capture opt-in, in memory only, and inactive at launch.
- Tag all mixer writes with the app's EventContext and receive external session
  volume/mute changes through Core Audio callbacks, including while disabled.
  Unregister callbacks, stop UI timers and release audio resources on exit.
  Callback handling must not block or create a separate background process.
- Preserve the approved VibeWare logo's pixels and transparency. Follow its
  lo-fi 1980s computing style without neon effects.

## Workflow

- Read `README.md` and inspect Git status before editing.
- Keep generated outputs and logs out of Git. Pin package versions.
- Do not add migration paths or compatibility branches solely for older app versions.
  Keep current user choices and runtime safety checks; they are not legacy support.
- Build after code edits and state separately whether an interactive microphone
  and playback test was actually performed.
- For owner-authorized documentation-only or repository-instruction-only delivery,
  commit and push directly on the current branch with `[skip ci]`. Do not create
  a branch or pull request or start CI. Preserve unrelated changes.
- For other changes, work on topic branches and propose pull requests for `main`.
  The repository owner controls merges and any direct bypass allowed by GitHub protection.
- Keep public documentation in English; discuss work with the owner in Italian.
- Follow VibeWare's shared `templates/APP-PUBLICATION.md` guidance for the main
  footer, About disclosure, repository/site/manifesto links and centered Terms
  and Privacy links. The shared legal pages cover applications that reference
  them; retain app-specific data handling in this repository's documentation.
- Keep main-window footer links visible for 30 seconds, then fade them out over
  3 seconds without shifting layout. Restore them on footer hover or keyboard
  focus and keep them visible during interaction. About links remain available.
- When the owner requests an installer, build the updated MSIX, terminate any
  running VoiceDucker instance, install the new package, and verify the installed
  version and deployment status. The owner has authorized forced termination for
  this workflow; no additional confirmation is needed. Preserve saved settings
  and restart the installed app with microphone capture off. An explicit request
  to compile only or skip installation takes precedence.

## Shared publication interface

Follow VibeWare templates/APP-PUBLICATION.md: centered About app · Terms · Privacy, first-person About disclosure, application and VibeWare GitHub links, personal site, and one-line This is VibeWare · Manifesto. Use the generic website legal pages with app-specific data documentation. Show main footer links for 30 seconds, fade over 3 seconds, preserve layout and reveal on hover or keyboard focus. Keep About links available.
