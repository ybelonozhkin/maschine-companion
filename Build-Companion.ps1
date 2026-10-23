$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (Get-Process -Name 'MaschineCompanion' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $PSScriptRoot 'bin\MaschineCompanion.exe') }) { throw 'Exit the companion from its tray menu before building.' }
New-Item -ItemType Directory -Force bin | Out-Null
if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'companion.ini'))) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'companion.example.ini') -Destination (Join-Path $PSScriptRoot 'companion.ini') }
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$uiaArgs = @("/reference:$framework\WPF\UIAutomationClient.dll", "/reference:$framework\WPF\UIAutomationTypes.dll", "/reference:$framework\WPF\WindowsBase.dll")
if (!(Test-Path -LiteralPath "$framework\csc.exe")) { throw "The x64 .NET Framework compiler was not found: $framework\csc.exe" }
$manifestArgs = @()
if (Test-Path -LiteralPath 'bin\CompanionMidi.dll') { $manifestArgs = @('/win32manifest:src\native\Midi.manifest') }
& "$framework\csc.exe" @manifestArgs @uiaArgs /nologo /target:exe /platform:x64 /out:bin\CompanionTests.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll src\Companion.cs src\WindowFocus.cs src\WindowClose.cs src\AssemblyInfo.cs src\StartupRegistration.cs src\SessionState.cs src\MikroInput.cs src\NativeMidi.cs src\AbletonIntegration.cs src\NoteRepeat.cs
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
& .\bin\CompanionTests.exe --self-test
if ($LASTEXITCODE -ne 0) { throw 'Self-test failed' }
& "$framework\csc.exe" @manifestArgs @uiaArgs /nologo /target:winexe /platform:x64 /out:bin\MaschineCompanion.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll src\Companion.cs src\WindowFocus.cs src\WindowClose.cs src\AssemblyInfo.cs src\StartupRegistration.cs src\SessionState.cs src\MikroInput.cs src\NativeMidi.cs src\AbletonIntegration.cs src\NoteRepeat.cs
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }

# Keep notices beside every locally built executable, including launcher-only builds.
Copy-Item -LiteralPath 'LICENSE','THIRD-PARTY-NOTICES.md' -Destination bin
$licenseOutput = Join-Path $PSScriptRoot 'bin\LICENSES'
New-Item -ItemType Directory -Force -Path $licenseOutput | Out-Null
foreach ($notice in 'microsoft-midi-MIT.txt','cppwinrt-MIT.txt','r00tman-MIT.txt','openav-BSD-3-Clause.txt') {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "LICENSES\$notice") -Destination (Join-Path $licenseOutput $notice)
}

& "$framework\csc.exe" /nologo /target:exe /platform:x64 /out:bin\MikroInputTests.exe src\MikroInput.cs tests\MikroInputTests.cs
if ($LASTEXITCODE -ne 0) { throw 'Mikro input test compilation failed' }
& .\bin\MikroInputTests.exe tests\fixtures\mikro-native-20261017.txt
if ($LASTEXITCODE -ne 0) { throw 'Mikro input tests failed' }
& "$framework\csc.exe" /nologo /target:exe /platform:x64 /out:bin\NoteRepeatTests.exe src\NoteRepeat.cs tests\NoteRepeatTests.cs
if ($LASTEXITCODE -ne 0) { throw 'Note repeat test compilation failed' }
& .\bin\NoteRepeatTests.exe
if ($LASTEXITCODE -ne 0) { throw 'Note repeat tests failed' }
