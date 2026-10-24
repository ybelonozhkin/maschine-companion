param([string]$UserLibrary)
$ErrorActionPreference = 'Stop'
if (!$UserLibrary) {
    $preferencesRoot = Join-Path $env:APPDATA 'Ableton'
    $candidates = Get-ChildItem -LiteralPath $preferencesRoot -Directory | Where-Object { $_.Name -match '^Live 12\.' } | Sort-Object { [version]($_.Name -replace '^Live ', '') } -Descending
    foreach ($candidate in $candidates) {
        $config = Join-Path $candidate.FullName 'Preferences\Library.cfg'
        if (!(Test-Path -LiteralPath $config)) { continue }
        $document = [xml](Get-Content -LiteralPath $config -Raw -Encoding UTF8)
        $entry = $document.SelectSingleNode('//UserLibrary/LibraryProject')
        if ($entry -and $entry.ProjectPath.Value -and $entry.ProjectName.Value) {
            $UserLibrary = Join-Path $entry.ProjectPath.Value $entry.ProjectName.Value
            break
        }
    }
}
if (!$UserLibrary -or !(Test-Path -LiteralPath $UserLibrary -PathType Container)) { throw 'Pass the existing Ableton User Library directory with -UserLibrary.' }
$destination = Join-Path $UserLibrary 'Remote Scripts\MikroCompanion'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($name in '__init__.py','model.py','activity.py','clip_activity.py','keyboard.py','editing.py','selection.py','mixer.py','navigation.py','devices.py','browser.py','sequencer.py','pattern.py','surface.py') {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "ableton\MikroCompanion\$name") -Destination (Join-Path $destination $name)
}
foreach ($name in 'LICENSE','THIRD-PARTY-NOTICES.md') {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $destination $name)
}
$licenseOutput = Join-Path $destination 'LICENSES'
New-Item -ItemType Directory -Path $licenseOutput -Force | Out-Null
foreach ($name in 'microsoft-midi-MIT.txt','cppwinrt-MIT.txt','r00tman-MIT.txt','openav-BSD-3-Clause.txt') {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "LICENSES\$name") -Destination (Join-Path $licenseOutput $name)
}
Write-Output "Installed: $destination"
Write-Output 'Restart Live, select MikroCompanion as a control surface, and choose Mikro Companion as its input.'
