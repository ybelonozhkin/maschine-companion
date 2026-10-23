$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$packages = @(Get-ChildItem -LiteralPath (Join-Path $root 'dist') -Directory -Filter 'MaschineCompanion-*-win-x64')
if ($packages.Count -ne 1) { throw 'Expected one staged distribution.' }
$package = $packages[0].FullName
$expected = @(
    'bin/MaschineCompanion.exe','bin/CompanionMidi.dll','bin/Windows.Devices.Midi2.dll','bin/Windows.Devices.Midi2.pri',
    'Start-Companion.cmd','Install-AbletonScript.ps1','companion.example.ini','companion.ini','README.md','CHANGELOG.md','ROADMAP.md',
    'LICENSE','THIRD-PARTY-NOTICES.md','GETTING-STARTED.md','CONTRIBUTING.md','BUILD-INFO.json','SHA256SUMS.txt','ableton/README.md','assets/maschine-button.svg'
)
foreach ($name in @('__init__.py','model.py','activity.py','clip_activity.py','keyboard.py','editing.py','selection.py','mixer.py','browser.py','sequencer.py','pattern.py','surface.py')) {
    $expected += "ableton/MikroCompanion/$name"
}
foreach ($name in @('microsoft-midi-MIT.txt','cppwinrt-MIT.txt','r00tman-MIT.txt','openav-BSD-3-Clause.txt')) { $expected += "LICENSES/$name" }
$actual = @(Get-ChildItem -LiteralPath $package -Recurse -File | ForEach-Object { $_.FullName.Substring($package.Length+1).Replace('\','/') })
$difference = @(Compare-Object ($expected | Sort-Object) ($actual | Sort-Object))
if ($difference.Count) { throw "Unexpected distribution contents: $($difference | Out-String)" }
if ((Get-FileHash -LiteralPath (Join-Path $package 'companion.ini')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $root 'companion.example.ini')).Hash) { throw 'The packaged config must be the example config.' }
$checked = @()
foreach ($line in Get-Content -LiteralPath (Join-Path $package 'SHA256SUMS.txt')) {
    if ($line -notmatch '^([0-9a-f]{64})  (.+)$') { throw 'Invalid checksum line.' }
    $digest = $Matches[1]; $relative = $Matches[2]
    if ($relative -notin $expected -or $relative -eq 'SHA256SUMS.txt' -or $relative -in $checked) { throw "Unexpected checksum entry: $relative" }
    if ((Get-FileHash -LiteralPath (Join-Path $package $relative) -Algorithm SHA256).Hash -ne $digest) { throw "Checksum mismatch: $relative" }
    $checked += $relative
}
if ($checked.Count -ne $expected.Count-1) { throw 'Not all files have checksums.' }
# Exercise the packaged installer against an empty temporary User Library.
$library = Join-Path ([IO.Path]::GetTempPath()) ('MikroCompanion-install-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $library | Out-Null
& (Join-Path $package 'Install-AbletonScript.ps1') -UserLibrary $library
$installed = Join-Path $library 'Remote Scripts\MikroCompanion'
foreach ($relative in $expected | Where-Object { $_ -like 'ableton/MikroCompanion/*' -or $_ -like 'LICENSES/*' -or $_ -in @('LICENSE','THIRD-PARTY-NOTICES.md') }) {
    $target = $relative -replace '^ableton/MikroCompanion/', ''
    if ((Get-FileHash -LiteralPath (Join-Path $installed $target)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $package $relative)).Hash) { throw "Installed file differs: $relative" }
}
$archive = "$package.zip"
if (Test-Path -LiteralPath $archive) {
    $archiveHash = Get-Content -LiteralPath (Join-Path $root 'dist\SHA256SUMS.txt') -Raw
    if ($archiveHash.Trim() -ne ('{0}  {1}' -f (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant(), [IO.Path]::GetFileName($archive))) { throw 'Archive checksum mismatch.' }
    $unpacked = Join-Path ([IO.Path]::GetTempPath()) ('MikroCompanion-extract-test-' + [guid]::NewGuid().ToString('N'))
    Expand-Archive -LiteralPath $archive -DestinationPath $unpacked
    $extractedPackage = Join-Path $unpacked ([IO.Path]::GetFileName($package))
    $extractedFiles = @(Get-ChildItem -LiteralPath $extractedPackage -Recurse -File)
    if ($extractedFiles.Count -ne $actual.Count) { throw 'Archive file count differs.' }
    foreach ($relative in $actual) {
        if ((Get-FileHash -LiteralPath (Join-Path $extractedPackage $relative)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $package $relative)).Hash) { throw "Archive file differs: $relative" }
    }
}
Write-Output "Distribution verified: $($actual.Count) files; hashes, default config and Live script installation passed."
