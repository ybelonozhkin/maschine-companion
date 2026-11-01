# Shared source list for the application and its integration tests.
$sourceRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'src'
foreach ($name in @('Companion.cs','WindowFocus.cs','WindowClose.cs','AssemblyInfo.cs','StartupRegistration.cs','SessionState.cs','MikroInput.cs','NativeMidi.cs','AbletonIntegration.cs','NoteRepeat.cs','NiControllerSession.cs')) {
    Join-Path $sourceRoot $name
}
Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'ableton') -Filter '*.cs' -File | Sort-Object Name | Select-Object -ExpandProperty FullName
Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'basic') -Filter '*.cs' -File | Sort-Object Name | Select-Object -ExpandProperty FullName
