# Contributing

Report bugs with the companion version, Windows and Live versions, and steps to reproduce. For controller problems, include the controller mode and whether another application was using it. Review logs for local paths before sharing them.

Changes and tests should describe the behavior they add or fix. Keep controller controls aligned with their printed labels, and document combinations in the Live guide. Contributions are accepted under the project's MIT license; include notices for any third-party material you add.

## Build and test

On Windows x64, build the launcher and run its logic, HID decoder and Note Repeat tests:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build-Companion.ps1
```

For the full Live build, install Visual Studio C++ Build Tools and Windows SDK 10.0.26100.0, then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build-Ableton.ps1
python -m unittest discover -s tests -p "test_ableton_*.py"
powershell -NoProfile -ExecutionPolicy Bypass -File .\Prepare-Distribution.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Test-Distribution.ps1
```

The MIDI SDK download is pinned by version and SHA-256 in `Build-Ableton.ps1`. Python 3.11 runs the script tests without Live. Exit the companion before rebuilding that copy. Use a fresh checkout for each distribution build; staging refuses to overwrite an existing distribution folder.

## GitHub builds

Pushes, pull requests and manual workflow runs build on Windows 2025 x64. One job checks the launcher without the MIDI bridge; another tests the Live script, builds the MIDI bridge and companion, and verifies the distribution's file list, hashes, default config and script installer. A successful run uploads the `windows-x64` artifact for 30 days.

Hosted runners do not test a physical Mikro, Live, MIDI Services loopback operation, or sleep recovery. Test those on a supported Windows installation before publishing a release.

## Release

1. Review the changes since the previous release in `CHANGELOG.md`, including beta labels and compatibility limits. Check the version in `src/AssemblyInfo.cs`.
2. Build and test from a clean commit. Check launch/exit, Live connection, pads, transport and USB reconnection on hardware.
3. Push a tag matching the application version, such as `v0.3.0`. Both build jobs must pass. The workflow attaches the portable build and its SHA-256 checksum to a **draft** GitHub Release, using the Unreleased changelog section as its description.
4. Download and test that build. Review the draft before publishing it manually. Date the changelog entry when the release is published.

`Prepare-Distribution.ps1 -Archive` creates the same ZIP locally. The package uses an explicit file list and copies the example config. Local `companion.ini`, logs, diagnostic binaries and research directories are excluded. `BUILD-INFO.json` records the source commit and whether its working tree had changes. Checksums identify the files; they do not sign the Windows executables.
