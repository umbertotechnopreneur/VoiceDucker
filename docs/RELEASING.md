# Releasing VoiceDucker

The current public portable version is **0.1.1**, with tag **v0.1.1-stable**.
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
After owner review, run it manually with `release=true`, `tag=v0.1.1-stable`,
and `draft=true`. Review both ZIPs and publish the draft when approved.
Each Windows job scans the restored NuGet archives, their extracted files, the
application output and final ZIP with Microsoft Defender. The generated scan
reports record package sources, versions, SHA-256 values, scan command and result,
and are attached to the release. A missing, disabled, failed or detecting scan
blocks the workflow.
A build, a passing archive check, and an interactive audio check are separate
facts. ARM64 runtime behavior needs an ARM64 machine.

Existing tags and release assets are never replaced by the workflow.
The release has no telemetry, signing certificate, or public MSIX package.

## WinGet

Proposed package identifier: `UmbertoGiacobbi.VoiceDucker`. Use package version
`0.1.1` and the exact final GitHub release asset URLs and SHA-256 hashes.
Windows x64 and ARM64 installers use `InstallerType: zip`,
`NestedInstallerType: portable`, and `NestedInstallerFiles` pointing to
`VoiceDucker.exe`. Set `ArchiveBinariesDependOnPath: true` and omit
`PortableCommandAlias`: the .NET app host must run from its installation folder
to find `VoiceDucker.dll` and the companion runtimes. WinGet adds that folder to
PATH; the launch command is `VoiceDucker.exe` in a newly opened terminal.
Minimum Windows version:
`10.0.22000.0`. Do not hash a separately rebuilt ZIP: use the published bytes.

Validate the version, locale and installer manifests with
`winget validate --manifest <manifest-directory>`, then submit a pull request
under `manifests/u/UmbertoGiacobbi/VoiceDucker/0.1.1/` in
[microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs).
Do not advertise `winget install --id UmbertoGiacobbi.VoiceDucker --exact` as
available until Microsoft merges and indexes the submission. The GitHub ZIPs
can be available first; review timing is outside the maintainer's control.

For a `Validation-Defender-Error`, inspect the attached validation artifacts
before changing release binaries. A failed signature update or scan operation
is different from a named threat detection. Scan the exact published ZIPs and
their extracted contents with Defender, preserve the results, and report them
on the submission. Request a fresh Microsoft validation if the detection cannot
be reproduced. Test installation and launch from a different working directory
in Windows Sandbox; keep microphone input disabled during this packaging check.

References: [submission](https://learn.microsoft.com/en-us/windows/package-manager/package/repository)
and [archive manifest schema](https://github.com/microsoft/winget-pkgs/blob/master/doc/manifest/schema/1.12.0/installer.md).
