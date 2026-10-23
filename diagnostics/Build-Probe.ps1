$projectRoot = Split-Path -Parent $PSScriptRoot
$ErrorActionPreference = 'Stop'
Set-Location $projectRoot
New-Item -ItemType Directory -Force bin | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
& "$framework\csc.exe" /nologo /target:exe /platform:x64 /out:bin\MaschineProbe.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:Accessibility.dll /reference:"$framework\WPF\UIAutomationClient.dll" /reference:"$framework\WPF\UIAutomationTypes.dll" /reference:"$framework\WPF\WindowsBase.dll" src\diagnostics\Probe.cs
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
& .\bin\MaschineProbe.exe --self-test
if ($LASTEXITCODE -ne 0) { throw 'Self-test failed' }
