param([switch]$Archive)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$assemblyInfo = Get-Content -LiteralPath src\AssemblyInfo.cs -Raw
$version = [regex]::Match($assemblyInfo, 'AssemblyInformationalVersion\("([0-9]+\.[0-9]+\.[0-9]+)"\)').Groups[1].Value
if (!$version) { throw 'Cannot read the application version.' }
if ($env:GITHUB_REF_TYPE -eq 'tag' -and $env:GITHUB_REF_NAME -ne "v$version") { throw 'Tag and application version do not match.' }
$name = "MaschineCompanion-$version-win-x64"
$distribution = Join-Path $PSScriptRoot 'dist'
$output = Join-Path $distribution $name
# Never copy an existing bin directory or reuse a possibly contaminated staging folder.
if (Test-Path -LiteralPath $output) { throw "Staging directory already exists: $output. Use a fresh checkout or rename the existing directory." }
$binaries = @('MaschineCompanion.exe','CompanionMidi.dll','Windows.Devices.Midi2.dll','Windows.Devices.Midi2.pri')
foreach ($nameRequired in $binaries) {
    if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot "bin\$nameRequired") -PathType Leaf)) { throw "Build the Live integration first: missing $nameRequired" }
}
New-Item -ItemType Directory -Path (Join-Path $output 'bin') -Force | Out-Null
foreach ($file in $binaries) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot "bin\$file") -Destination (Join-Path $output 'bin') }
foreach ($file in @('Start-Companion.cmd','Install-AbletonScript.ps1','companion.example.ini','README.md','CHANGELOG.md','ROADMAP.md','CONTRIBUTING.md','LICENSE','THIRD-PARTY-NOTICES.md','GETTING-STARTED.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $output
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'companion.example.ini') -Destination (Join-Path $output 'companion.ini')
New-Item -ItemType Directory -Path (Join-Path $output 'ableton\MikroCompanion'),(Join-Path $output 'launcher'),(Join-Path $output 'LICENSES'),(Join-Path $output 'assets') -Force | Out-Null
foreach ($file in @('README.md','CONFIGURATION.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "launcher\$file") -Destination (Join-Path $output 'launcher')
}
$scriptFiles = @('__init__.py','model.py','activity.py','clip_activity.py','keyboard.py','editing.py','selection.py','mixer.py','browser.py','sequencer.py','pattern.py','surface.py')
foreach ($file in $scriptFiles) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "ableton\MikroCompanion\$file") -Destination (Join-Path $output 'ableton\MikroCompanion')
}
foreach ($file in @('README.md')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot "ableton\$file") -Destination (Join-Path $output 'ableton') }
foreach ($file in @('microsoft-midi-MIT.txt','cppwinrt-MIT.txt','r00tman-MIT.txt','openav-BSD-3-Clause.txt')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "LICENSES\$file") -Destination (Join-Path $output 'LICENSES')
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets\maschine-button.svg') -Destination (Join-Path $output 'assets')
$commit = (& git rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the source commit.' }
$dirty = @(& git status --porcelain).Count -gt 0
@{version=$version;commit=$commit;sourceModified=$dirty;platform='windows-x64';signed=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'BUILD-INFO.json') -Encoding UTF8
$hashes = foreach ($file in Get-ChildItem -LiteralPath $output -Recurse -File | Sort-Object FullName) {
    $relative = $file.FullName.Substring($output.Length+1).Replace('\','/')
    '{0}  {1}' -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
}
$hashes | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ASCII
# Use the changelog entry matching the application version for the draft release.
$changelog = Get-Content -LiteralPath CHANGELOG.md -Raw
$entryPattern = '(?ms)^## \[' + [regex]::Escape($version) + '\][^\r\n]*\r?\n(.*?)(?=^## \[|\z)'
$notes = [regex]::Match($changelog, $entryPattern).Groups[1].Value.Trim()
if (!$notes) { throw "The changelog entry for $version is missing or empty." }
New-Item -ItemType Directory -Force -Path $distribution | Out-Null
$notes | Set-Content -LiteralPath (Join-Path $distribution 'RELEASE-NOTES.md') -Encoding UTF8
if ($Archive) {
    $archivePath = Join-Path $distribution "$name.zip"
    if (Test-Path -LiteralPath $archivePath) { throw "Archive already exists: $archivePath" }
    Compress-Archive -LiteralPath $output -DestinationPath $archivePath
    '{0}  {1}' -f (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant(), "$name.zip" | Set-Content -LiteralPath (Join-Path $distribution 'SHA256SUMS.txt') -Encoding ASCII
}
Write-Output "Prepared: $output"
