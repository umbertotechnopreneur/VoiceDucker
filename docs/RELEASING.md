# Releasing VoiceDucker

The first public portable version is **0.1.0**, with tag **v0.1.0-stable**.
The older 1.0.x values were development/MSIX build numbers. Do not install this
portable release as a downgrade over an existing development MSIX.

## Build and package

On Windows with the .NET 10 SDK and Windows SDK tools:

```powershell
pwsh -NoProfile -File scripts/build.ps1 -Architecture x64 -Configuration Release
pwsh -NoProfile -File scripts/package-portable.ps1 -Runtime win-x64
pwsh -NoProfile -File scripts/build.ps1 -Architecture ARM64 -Configuration Release
pwsh -NoProfile -File scripts/package-portable.ps1 -Runtime win-arm64
```

Do not pass `-Run` to prepare a release. Existing running applications are not
stopped. ZIPs include the app, its companion runtimes, README, MIT license,
screenshot and notices copied from restored NuGet dependencies. Keep each ZIP's
companion files together. Packaging refuses to overwrite an existing ZIP.

## GitHub

Open a pull request for source changes. The **Portable Windows ZIPs** workflow
builds both architectures and checks archive integrity and SHA-256 checksums.
After owner review, run it manually with `release=true`, `tag=v0.1.0-stable`,
and `draft=true`. Review both ZIPs and publish the draft when approved.
A build, a passing archive check, and an interactive audio check are separate
facts. ARM64 runtime behavior needs an ARM64 machine.

Existing tags and release assets are never replaced by the workflow.
The release has no telemetry, signing certificate, or public MSIX package.

## WinGet

Proposed package identifier: `UmbertoGiacobbi.VoiceDucker`. Use package version
`0.1.0` and the exact final GitHub release asset URLs and SHA-256 hashes.
Windows x64 and ARM64 installers use `InstallerType: zip`,
`NestedInstallerType: portable`, and `NestedInstallerFiles` pointing to
`VoiceDucker.exe`, with command alias `voice-ducker`. Minimum Windows version:
`10.0.22000.0`. Do not hash a separately rebuilt ZIP: use the published bytes.

Validate the version, locale and installer manifests with
`winget validate --manifest <manifest-directory>`, then submit a pull request
under `manifests/u/UmbertoGiacobbi/VoiceDucker/0.1.0/` in
[microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs).
Do not advertise `winget install --id UmbertoGiacobbi.VoiceDucker --exact` as
available until Microsoft merges and indexes the submission. The GitHub ZIPs
can be available first; review timing is outside the maintainer's control.

References: [submission](https://learn.microsoft.com/en-us/windows/package-manager/package/repository)
and [archive manifest schema](https://github.com/microsoft/winget-pkgs/blob/master/doc/manifest/schema/1.9.0/installer.md).
