param([string]$SdkPath)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Set-Location $PSScriptRoot
New-Item -ItemType Directory -Force bin | Out-Null
if (Get-Process -Name MaschineCompanion -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $PSScriptRoot 'bin\MaschineCompanion.exe') }) { throw 'Exit the companion before building.' }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (!(Test-Path -LiteralPath $vswhere)) { throw 'Install Visual Studio C++ Build Tools and Windows SDK 10.0.26100.0.' }
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$visualStudio) { throw 'Visual Studio C++ Build Tools were not found.' }
$windowsSdk = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
$cppwinrt = Join-Path $windowsSdk 'bin\10.0.26100.0\x64\cppwinrt.exe'
if (!(Test-Path -LiteralPath $cppwinrt)) { throw 'Windows SDK 10.0.26100.0 is required.' }
if (!$SdkPath) {
    $SdkPath = Join-Path $PSScriptRoot 'bin\dependencies\windows-midi'
    if (!(Test-Path -LiteralPath (Join-Path $SdkPath 'ref\native\Windows.Devices.Midi2.winmd'))) {
        $archive = Join-Path $PSScriptRoot 'bin\windows-midi-sdk.zip'
        Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/microsoft/MIDI/releases/download/inbox-preview-11/Windows.Devices.Midi2.0.99.92-preview.11.nupkg' -OutFile $archive
        if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne '13AFC6E6DA2ADAC309AB8C174352F46F1C0EBE2D79E65475BD6274DEFB73DF74') { throw 'Windows MIDI SDK checksum mismatch.' }
        Expand-Archive -LiteralPath $archive -DestinationPath $SdkPath -Force
    }
}
$SdkPath = (Resolve-Path -LiteralPath $SdkPath).Path
$generated = Join-Path $PSScriptRoot 'bin\midi-headers'
& $cppwinrt -input (Join-Path $SdkPath 'ref\native\Windows.Devices.Midi2.winmd') -reference (Join-Path $windowsSdk 'UnionMetadata\10.0.26100.0\Windows.winmd') -output $generated
if ($LASTEXITCODE -ne 0) { throw 'Windows MIDI headers could not be generated.' }
$commands = @"
@echo off
call "$visualStudio\VC\Auxiliary\Build\vcvars64.bat" >nul
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /EHsc /MD /LD /I "$generated" /I "$windowsSdk\Include\10.0.26100.0\cppwinrt" "$PSScriptRoot\src\native\CompanionMidi.cpp" /Fo"$PSScriptRoot\bin\CompanionMidi.obj" /Fe"$PSScriptRoot\bin\CompanionMidi.dll" /link windowsapp.lib
"@
$commandFile = Join-Path $PSScriptRoot 'bin\build-native-midi.cmd'
Set-Content -LiteralPath $commandFile -Value $commands -Encoding ASCII
& cmd.exe /c "`"$commandFile`""
if ($LASTEXITCODE -ne 0) { throw 'Native MIDI bridge compilation failed.' }
Copy-Item -LiteralPath (Join-Path $SdkPath 'runtimes\win-x64\native\Windows.Devices.Midi2.dll') -Destination bin
Copy-Item -LiteralPath (Join-Path $SdkPath 'runtimes\win-x64\native\Windows.Devices.Midi2.pri') -Destination bin
& (Join-Path $PSScriptRoot 'Build-Companion.ps1')
