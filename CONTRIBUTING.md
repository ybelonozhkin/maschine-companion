# Contributing

Report bugs with the companion version, Windows and Live versions, and steps to reproduce. For controller problems, include the controller mode and whether another application was using it. Review logs for local paths before sharing them.

Changes and tests should describe the behavior they add or fix. Keep controller controls aligned with their printed labels, and document combinations in the Live guide. Contributions are accepted under the project's MIT license; include notices for any third-party material you add.

## Build and test

Repository layout: `src/` contains the companion, `ableton/` the Live script, `scripts/` the build and packaging commands, and `tests/` the automated checks. `diagnostics/` contains troubleshooting tools. Runtime output goes to ignored `bin/`, `dist/` and `logs/` directories.

Clone the repository or extract a release's **Source code** archive into a writable folder. Use the release tag when building a published version; the development branch may contain newer changes. Open Windows PowerShell in the source folder.

The launcher build uses the x64 .NET Framework 4.x compiler included with Windows. The full Live build also needs Visual Studio C++ Build Tools with **Desktop development with C++**, Windows SDK **10.0.26100.0**, and internet access to download the pinned MIDI SDK. Install Python **3.11** to run the Live script tests. A controller and Live are not needed for these build-time tests.

On Windows x64, build the launcher and run its logic, HID decoder and Note Repeat tests:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Companion.ps1
```

For the full Live build:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Ableton.ps1
python -m unittest discover -s tests -p "test_ableton_*.py"
```

To prepare a distribution from a Git clone:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Prepare-Distribution.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Test-Distribution.ps1
```

The executable is written to `bin\MaschineCompanion.exe`. `scripts/Prepare-Distribution.ps1` stages the complete build in `dist`; add `-Archive` to produce the portable ZIP. Source archives have no Git metadata, so clone the repository to run the distribution step, which records the source commit. You can build and run the app from either source format.

The MIDI SDK download is pinned by version and SHA-256 in `scripts/Build-Ableton.ps1`. Exit the companion before rebuilding that copy. Use a fresh checkout for each distribution build; staging refuses to overwrite an existing distribution folder.

On a PC with Windows MIDI Services installed, exit the companion and run `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Test-Midi.ps1` to check MIDI delivery and note releases. This test creates and removes a temporary MIDI pair.

API references: [Microsoft MIDI SDK](https://github.com/microsoft/MIDI/releases/tag/inbox-preview-11) and [Live Clip API](https://docs.cycling74.com/apiref/lom/clip/). See also [Live's keyboard shortcuts](https://www.ableton.com/en/live-manual/12/live-keyboard-shortcuts/) and [remote script installation](https://help.ableton.com/hc/en-us/articles/209072009-Installing-third-party-remote-scripts).

## GitHub builds

Pushes, pull requests and manual workflow runs build on Windows 2025 x64. One job checks the launcher without the MIDI bridge; another tests the Live script, builds the MIDI bridge and companion, and verifies the distribution's file list, hashes, default config and script installer. A successful run uploads the `windows-x64` artifact for 30 days.

Hosted runners do not test a physical Mikro, Live, MIDI Services loopback operation, or sleep recovery. Test those on a supported Windows installation before publishing a release.

## Release

1. Review the version's entry in `CHANGELOG.md`, including beta labels and compatibility limits. Its version must match `src/AssemblyInfo.cs`.
2. Build and test from a clean commit. Check launch/exit, Live connection, pads, transport and USB reconnection on hardware.
3. Push a tag matching the application version, such as `v0.4.0`. Both build jobs must pass. The workflow attaches the portable build and its SHA-256 checksum to a **draft** GitHub Release, using the matching version's changelog entry as its description.
4. Download and test that build. Review the draft before publishing it manually. Date the changelog entry when the release is published.

`scripts/Prepare-Distribution.ps1 -Archive` creates the same ZIP locally. The package uses an explicit file list and copies the example config. Local `companion.ini`, logs, diagnostic binaries and research directories are excluded. `BUILD-INFO.json` records the source commit and whether its working tree had changes. Checksums identify the files; they do not sign the Windows executables.
