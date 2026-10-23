param([ValidateRange(1,1800)][int]$Seconds = 300)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force bin | Out-Null
& $compiler /nologo /target:winexe /platform:x64 /out:bin\HidCapture.exe /reference:System.Windows.Forms.dll src\diagnostics\HidCapture.cs
if ($LASTEXITCODE -ne 0) { throw 'HID capture compilation failed' }
Start-Process -FilePath (Join-Path $PSScriptRoot 'bin\HidCapture.exe') -ArgumentList "$Seconds" -WorkingDirectory $PSScriptRoot -WindowStyle Hidden
Write-Output "Passive HID capture started for $Seconds seconds. Logs: $PSScriptRoot\logs\hid-native-*.log"
