# Run the downloaded build

Download `MaschineCompanion-<version>-win-x64.zip` from the project's [Releases page](https://github.com/ybelonozhkin/maschine-companion/releases). Choose this file to run the app; the **Source code** downloads are for compiling it yourself.

Extract the entire folder to a writable location before starting the app. Keep `bin`, `ableton`, `launcher` and `LICENSES` together with the files at the top level.

## Requirements

- Windows 11 x64 and Maschine Mikro MK3. Other controllers and macOS are not supported by this build.
- The NI controller driver. The default launcher configuration expects Maschine 3 at its usual installation path; edit `Executable` in `companion.ini` if yours is elsewhere.
- For Live integration: Ableton Live 12, Windows 11 25H2 or later, and Windows MIDI Services with loopback support. See Microsoft's [MIDI Services setup](https://microsoft.github.io/MIDI/) and [system requirements](https://microsoft.github.io/MIDI/kb/minimum-requirements/). Tested on Live 12.4.6 and 12.4.7 Standard; other setups need compatibility testing.
- The latest Microsoft Visual C++ v14 Redistributable, x64: [Microsoft download](https://aka.ms/vc14/vc_redist.x64.exe). Install or update it before using the Live integration. An older runtime may cause missing-DLL or entry-point errors. See [Microsoft's runtime requirements](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist).

Build tools and Python are not required to run this download. Put Mikro in native mode; if it is in MIDI mode, use Shift + Maschine to switch back.

## Maschine launcher

1. Check the `Executable` path in `companion.ini`.
2. Run `Start-Companion.cmd`. The app appears in the system tray; its icon may be hidden under the tray overflow arrow.
3. Connect Mikro. The Maschine button lights when launching is available. Hold it to launch Maschine; hold it again to request a normal exit.

See [launcher controls and the tray menu](launcher/README.md) and the [configuration reference](launcher/CONFIGURATION.md) for more options.

## Ableton Live installation

1. Close Live and exit the companion from its tray menu.
2. Install the requirements listed above, including Windows MIDI Services and the current Visual C++ runtime.
3. Open `companion.ini` in a text editor. Set `EnableAbleton=true` and save it. Keep the `Executable` path pointing to your installed Maschine application.
4. Open PowerShell in the extracted folder and install the Live script:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-AbletonScript.ps1
   ```

   The installer finds your Live 12 User Library. If it cannot find it, pass its existing path:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-AbletonScript.ps1 -UserLibrary "D:\Music\Ableton\User Library"
   ```

5. Run `Start-Companion.cmd`, then open Live. This order makes the companion's MIDI port available when Live starts.
6. Open Live's MIDI preferences and set one Control Surface row to:

   | Control Surface | Input | Output |
   | --- | --- | --- |
   | MikroCompanion | Mikro Companion | None |

7. In **Input Ports**, enable **Track** for the **Mikro Companion** input. Leave **Mikro Companion Send** unassigned, with Track and Sync off. Keep the companion's output Track and Sync off.
8. Select a MIDI track with an instrument. Set its MIDI input to **Mikro Companion**, or use **All Ins**. Arm the track and check its monitoring setting, then play the pads. Recording requires Arm; the companion does not arm tracks automatically.

The input named **Maschine Mikro MK3** belongs to NI's MIDI mode. Use **Mikro Companion** for this integration. Keep other controllers assigned to their existing control surfaces.

The [controls guide](ableton/README.md) covers playing, recording, sequencing and browsing. Arp Rhythm and track navigation are beta features.

Choose Pause in the tray before loading Maschine as a plug-in. Alternatively,
opt into [experimental automatic handoff](ableton/README.md#experimental-automatic-handoff)
with `EnableNativeHandoff=true`, then restart the companion. This requires NI's
Host Integration Agent. Multiple Maschine instances use manual Controller-menu
selection inside Maschine.

## Update or remove

Close Live and exit the companion before replacing files. Extract an update into a new folder, copy your previous `companion.ini` into it, and run the new script installer before reopening Live. If Start with Windows was enabled, disable it in the old copy before moving folders and enable it again in the new copy.

To remove the app, disable Start with Windows, exit the companion and remove its extracted folder. In Live, clear its Control Surface row. With Live closed, remove `Remote Scripts\MikroCompanion` from your User Library.

## Unsigned builds

The build is unsigned. Windows may show an unknown-publisher warning; Smart App Control or workplace policies may block it. Download from this repository and check the SHA-256 checksum. See [Microsoft's explanation](https://learn.microsoft.com/windows/apps/package-and-deploy/smartscreen-reputation).

`BUILD-INFO.json` identifies the version and source commit; `SHA256SUMS.txt` lists file checksums.

For source builds, see [Build and test](CONTRIBUTING.md#build-and-test). Build failures and hardware problems can be reported in the repository's Issues. Include the version and a short reproduction; review logs for local paths before sharing them.
