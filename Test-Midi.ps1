$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (Get-Process -Name MaschineCompanion -ErrorAction SilentlyContinue) { throw 'Exit the companion before running the MIDI loopback test.' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Split-Path -Parent $compiler
$uiaArgs = @("/reference:$framework\WPF\UIAutomationClient.dll", "/reference:$framework\WPF\UIAutomationTypes.dll", "/reference:$framework\WPF\WindowsBase.dll")
& $compiler @uiaArgs /nologo /target:exe /platform:x64 /win32manifest:src\native\Midi.manifest /main:NativeMidiTests /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /out:bin\NativeMidiTests.exe src\NativeMidi.cs src\MikroInput.cs src\AbletonIntegration.cs src\NoteRepeat.cs src\Companion.cs src\WindowFocus.cs src\WindowClose.cs src\StartupRegistration.cs src\SessionState.cs tests\NativeMidiTests.cs
if ($LASTEXITCODE -ne 0) { throw 'MIDI test compilation failed' }
& .\bin\NativeMidiTests.exe
if ($LASTEXITCODE -ne 0) { throw 'MIDI loopback test failed' }
