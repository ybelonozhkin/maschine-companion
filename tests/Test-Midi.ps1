param([switch]$BasicOnly)
$projectRoot = Split-Path -Parent $PSScriptRoot
$ErrorActionPreference = 'Stop'
Set-Location $projectRoot
if (Get-Process -Name MaschineCompanion -ErrorAction SilentlyContinue) { throw 'Exit the companion before running the MIDI loopback test.' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Split-Path -Parent $compiler
$uiaArgs = @("/reference:$framework\WPF\UIAutomationClient.dll", "/reference:$framework\WPF\UIAutomationTypes.dll", "/reference:$framework\WPF\WindowsBase.dll")
$companionSources = @(& (Join-Path $projectRoot 'scripts\Get-CompanionSources.ps1'))
& $compiler @uiaArgs /nologo /target:exe /platform:x64 /win32manifest:src\native\Midi.manifest /main:NativeMidiTests /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /out:bin\NativeMidiTests.exe @companionSources tests\NativeMidiTests.cs tests\BasicMidiIntegrationTests.cs
if ($LASTEXITCODE -ne 0) { throw 'MIDI test compilation failed' }
if ($BasicOnly) { & .\bin\NativeMidiTests.exe --basic-only } else { & .\bin\NativeMidiTests.exe }
if ($LASTEXITCODE -ne 0) { throw 'MIDI loopback test failed' }
