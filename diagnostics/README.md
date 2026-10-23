# Developer diagnostics

Run these commands from the repository root. Output goes to `bin/` and `logs/`.

| Tool | Purpose |
| --- | --- |
| `Build-Probe.ps1` | Build the Windows UI probe and run its self-tests. |
| `Run-Probe.cmd` | Open the compiled UI probe. |
| `Capture-Hid.cmd` | Capture native controller input for troubleshooting. |
| `Observe-NIOwnership.ps1` | Record NI/DAW process and named-pipe metadata. This is not a verified controller ownership signal. |

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\diagnostics\Build-Probe.ps1
.\diagnostics\Run-Probe.cmd
.\diagnostics\Capture-Hid.cmd
```

These tools are for development and are excluded from the portable download. Review captured logs before sharing them.
