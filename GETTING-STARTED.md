# Run the downloaded build

Use the Windows x64 build from this repository's Releases page. Extract the entire folder to a writable location. Keep `bin`, `ableton` and `LICENSES` together with the files at the top level.

## Requirements

- Windows 11 x64 and Maschine Mikro MK3 in native mode.
- The NI controller driver. The default launcher configuration expects Maschine 3 at its usual installation path; edit `Executable` in `companion.ini` if yours is elsewhere.
- For Live integration: Ableton Live 12 and Windows MIDI Services with loopback support. Tested on Live 12.4.6 Standard; other setups need compatibility testing.
- The latest Microsoft Visual C++ v14 Redistributable, x64: [Microsoft download](https://aka.ms/vc14/vc_redist.x64.exe). Install or update it before using the Live integration. An older runtime may cause missing-DLL or entry-point errors. See [Microsoft's runtime requirements](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist).

Build tools are not required to run this download.

## Start

1. Check the `Executable` path in `companion.ini`.
2. Run `Start-Companion.cmd`. The app appears in the system tray; its icon may be hidden under the tray overflow arrow.
3. Connect Mikro. The Maschine button lights when launching is available. Hold it to launch Maschine; hold it again to request a normal exit.

For Live, close Live and exit the companion, set `EnableAbleton=true` in `companion.ini`, then open PowerShell in the extracted folder and install the script:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-AbletonScript.ps1
```

Start the companion before reopening Live. Follow the [port settings and controls](ableton/README.md#setup), starting at the MIDI preferences step. Arp Rhythm and track navigation are beta features. Track arming is manual.

Choose Pause in the tray before loading Maschine as a plug-in. Resume after releasing the controller from Maschine.

## Unsigned builds

The EXE and our native DLL are unsigned. Windows may warn about an unknown publisher. Check that the download came from this repository and verify its SHA-256 checksum before deciding whether to run it. Smart App Control or a managed computer's policies may block the build entirely. We do not recommend turning off system-wide protection to use the app. [Microsoft explains the distinction](https://learn.microsoft.com/windows/apps/package-and-deploy/smartscreen-reputation).

`BUILD-INFO.json` records the version and source commit. `SHA256SUMS.txt` lists hashes for the files in the extracted build. These identify the downloaded files; they are not a Windows publisher signature.

For source builds, see [README](README.md). Build failures and hardware problems can be reported in the repository's Issues. Include the version and a short reproduction; review logs for local paths before sharing them.
