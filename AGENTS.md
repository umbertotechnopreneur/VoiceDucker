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
- Preserve user mixer changes. Restore only a level the app still owns and
  report failures instead of claiming audio was restored.
- Keep microphone capture opt-in, in memory only, and inactive at launch.
- Preserve the approved VibeWare logo's pixels and transparency. Follow its
  lo-fi 1980s computing style without neon effects.

## Workflow

- Read `README.md` and inspect Git status before editing.
- Keep generated outputs and logs out of Git. Pin package versions.
- Build after code edits and state separately whether an interactive microphone
  and playback test was actually performed.
- Work on topic branches and propose pull requests for `main`. The repository
  owner controls merges and any direct bypass allowed by GitHub protection.
- Keep public documentation in English; discuss work with the owner in Italian.
- When the owner requests an installer, build the updated MSIX, terminate any
  running VoiceDucker instance, install the new package, and verify the installed
  version and deployment status. The owner has authorized forced termination for
  this workflow; no additional confirmation is needed. Preserve saved settings
  and restart the installed app with microphone capture off. An explicit request
  to compile only or skip installation takes precedence.

## Shared publication interface

Follow VibeWare templates/APP-PUBLICATION.md: centered About app · Terms · Privacy, first-person About disclosure, application and VibeWare GitHub links, personal site, and one-line This is VibeWare · Manifesto. Use the generic website legal pages with app-specific data documentation. Show main footer links for 30 seconds, fade over 3 seconds, preserve layout and reveal on hover or keyboard focus. Keep About links available.
