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
  changes. Capture new sessions when first observed. These are safety restoration
  levels for Disable and exit. Normal listening volume is separate: the slider
  displays actual volume and edits it through the model; the worker derives normal
  volume using the current reduction. Voice applies that reduction to normal volume,
  and silence returns to it. Never
  derive normal volume from an intermediate fade or an app-generated callback.
  Report restoration failures instead of claiming audio was restored.
- Keep application state and rules in the application object model. The UI renders
  immutable model snapshots and sends typed commands. It must not read the mixer,
  own settings or license policy, classify microphone levels, or control capture
  directly. UI-only focus, editing, scroll, window geometry and animation state
  may remain in view adapters.
- Use one app-lifetime audio worker for capture lifecycle, observations, detection,
  session discovery and mixer writes, including discovery while capture is off.
- Keep microphone capture opt-in and in memory only. The saved **Enable microphone
  on startup** opt-in is off by default, independent of **Start with Windows**, and
  applies to every app launch, including manual launches. Start capture only after
  the playback snapshot and normal license checks. Changing the preference does
  not start microphone capture immediately.
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
- Build after code edits. The owner performs tests; do not run automated or
  interactive tests unless explicitly requested for the current task.
- Announce any planned app launch, close, termination or restart before doing it,
  including during installation. Do not open or close the app, toggle microphone
  monitoring or change playback volumes for validation. These actions can disturb
  the owner's live audio session; leave functional verification to the owner.
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
- When the owner requests an installer, build the updated MSIX and announce the
  planned app stop/restart before executing it. Forced termination is authorized
  for this deployment workflow without another confirmation. Install the package
  and verify its version and deployment status without UI tests. Preserve saved
  settings and restart the app according to the saved **Enable microphone on
  startup** preference as announced. Do not perform additional validation
  restarts. An explicit request to compile only or
  skip installation takes precedence.

## Shared publication interface

Follow VibeWare templates/APP-PUBLICATION.md: centered About app · Terms · Privacy, first-person About disclosure, application and VibeWare GitHub links, personal site, and one-line This is VibeWare · Manifesto. Use the generic website legal pages with app-specific data documentation. Show main footer links for 30 seconds, fade over 3 seconds, preserve layout and reveal on hover or keyboard focus. Keep About links available.
