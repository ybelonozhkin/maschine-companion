$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (Get-Process -Name 'MaschineCompanion' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $PSScriptRoot 'bin\MaschineCompanion.exe') }) { throw 'Exit the companion from its tray menu before building.' }
New-Item -ItemType Directory -Force bin | Out-Null
if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'companion.ini'))) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'companion.example.ini') -Destination (Join-Path $PSScriptRoot 'companion.ini') }
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path -LiteralPath "$framework\csc.exe")) { throw "The x64 .NET Framework compiler was not found: $framework\csc.exe" }
& "$framework\csc.exe" /nologo /target:exe /platform:x64 /out:bin\CompanionTests.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll src\Companion.cs src\WindowFocus.cs src\WindowClose.cs src\AssemblyInfo.cs src\StartupRegistration.cs src\SessionState.cs
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
& .\bin\CompanionTests.exe --self-test
if ($LASTEXITCODE -ne 0) { throw 'Self-test failed' }
& "$framework\csc.exe" /nologo /target:winexe /platform:x64 /out:bin\MaschineCompanion.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll src\Companion.cs src\WindowFocus.cs src\WindowClose.cs src\AssemblyInfo.cs src\StartupRegistration.cs src\SessionState.cs
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
