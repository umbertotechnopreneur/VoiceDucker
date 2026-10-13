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

## Microsoft Store preparation

The reserved Store identity is in `Properties/StoreIdentity.json`. Build an unsigned
upload package with `pwsh -NoProfile -File scripts/package-msix.ps1 -Store -Architecture x64`
(or `ARM64`). The script generates a separate manifest, checks the packaged
Name, Publisher, PublisherDisplayName, architecture and version, and rejects
installation or local signing in Store mode. Microsoft signs accepted Store
packages. The source manifest retains the local test identity for sideload updates.

Before submission, run Windows App Certification Kit and test installation and audio
behavior on a clean machine. A local signed MSIX requires its test certificate's
public key to be trusted on the test machine; never distribute its private key.
The owner manages virtual-machine testing.

Create a submission in Partner Center, then configure Store listings. Export listings
as CSV UTF-8, preserve the Field/ID/Type columns, and import a folder containing
the edited CSV and image assets. Localized Store descriptions do not imply that
the app interface supports those languages; VoiceDucker currently uses English UI.
Use the generic website Terms and Privacy URLs, with app-specific data handling
documented here. Follow VibeWare's `templates/APP-PUBLICATION.md` for About and links.

Store screenshots must meet Store-specific image requirements; the README marketing
board is not the Store screenshot template. Prepare clean UI screenshots and a
300 × 300 app tile. Optional hero art is 1920 × 1080 or 3840 × 2160 without text
or application UI. Poster/box art guidance for games is not a requirement for this app.

The Store edition uses `Windows.Services.Store.StoreContext`, initialized with the
WinUI window handle, and the app's Store ID `9P2F2FCRHS85`. Configure a time-limited
30-day trial and the $1.99 base US one-time purchase price in Partner Center.
The app uses Store-provided active/trial/expiration data and the Windows license
cache offline; it does not persist its own trial start or reset trial on reinstall.
The footer's Purchase link opens the Store product page, which displays market
pricing. The title shows days remaining; trial expiry displays a one-time dialog
with Store and GitHub source links. Trial expiry
and Store license changes stop microphone capture and restore owned audio.

License policy boundary tests run with the existing settings test script. Actual
Store trial acquisition, purchase, cancellation, offline cache, expiration and
restoration must also be tested using a Store-installed package and test accounts.
A sideloaded package cannot certify the Store purchase flow. Run WACK before
certification, then use a private-audience Store release to acquire a real license
and complete the Store checks before broad public availability. Source and portable
distribution remain MIT-licensed.

References: [package requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements),
[listing import/export](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/import-and-export-store-listings),
[Store images](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/screenshots-and-images).

## WinGet publication

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
