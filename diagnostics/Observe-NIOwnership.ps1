param([ValidateRange(1,3600)][int]$DurationSeconds=180)
$projectRoot = Split-Path -Parent $PSScriptRoot
$ErrorActionPreference='Stop'
# Metadata-only: no pipe connection, IPC messages, HID reads/writes, or ownership requests.
$config=Join-Path $projectRoot 'companion.ini'
$rules=@('Ableton Live*')
if(Test-Path -LiteralPath $config){
 $line=Get-Content -LiteralPath $config | Where-Object {$_ -match '^\s*DawProcesses\s*='} | Select-Object -Last 1
 if($line){$rules=@(($line -split '=',2)[1] -split ';' | ForEach-Object {$_.Trim() -replace '(?i)\.exe$',''} | Where-Object {$_})}
}
$folder=Join-Path $projectRoot 'logs'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$path=Join-Path $folder ('ni-ownership-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.jsonl')
$timer=[Diagnostics.Stopwatch]::StartNew()
$last=''
Write-Host ('Read-only metadata observer; output: '+$path)
Write-Host 'No state in this log is a verified hardware ownership signal. Press Ctrl+C to stop.'
while($timer.Elapsed.TotalSeconds -lt $DurationSeconds){
 $candidates=@()
 foreach($process in @(Get-Process)){
  try{
   $name=$process.ProcessName
   $daw=$false
   foreach($rule in $rules){if($name -like $rule){$daw=$true;break}}
   if(!$daw -and $name -notin @('Maschine 3','Maschine3','Maschine 2','Maschine2','NIHostIntegrationAgent','NIHardwareService','NIHardwareAgent')){continue}
   $modules=@();$inspection='not requested'
   if($daw){
    try{$modules=@($process.Modules | Where-Object {$_.ModuleName -like 'Maschine*'} | Select-Object -ExpandProperty ModuleName | Sort-Object -Unique);$inspection='available'}
    catch{$inspection='unavailable: '+$_.Exception.GetType().Name}
   }
   $candidates+=[ordered]@{name=$name;pid=$process.Id;role=$(if($daw){'DAW candidate'}else{'NI process'});maschineModules=$modules;moduleInspection=$inspection}
  }catch{continue}finally{$process.Dispose()}
 }
 $pipes=@();$pipeInspection='available'
 try{$pipes=@(Get-ChildItem -LiteralPath '\\.\pipe\' | Where-Object {$_.Name -like 'com.native-instruments*' -or $_.Name -like 'NIHWS1700*' -or $_.Name -like 'NIHWMaschineMikroMK3*'} | Select-Object -ExpandProperty Name | Sort-Object)}
 catch{$pipeInspection='unavailable: '+$_.Exception.GetType().Name}
 $state=[ordered]@{candidates=@($candidates | Sort-Object {$_['pid']});pipeInspection=$pipeInspection;pipes=$pipes}
 $json=$state | ConvertTo-Json -Depth 6 -Compress
 if($json -ne $last){
  $record=[ordered]@{timestamp=[DateTimeOffset]::Now.ToString('o');metadata=$state}
  $record | ConvertTo-Json -Depth 7 -Compress | Add-Content -LiteralPath $path -Encoding UTF8
  Write-Host ([DateTimeOffset]::Now.ToString('HH:mm:ss')+' metadata changed; candidate processes='+$candidates.Count+' NI pipes='+$pipes.Count)
  $last=$json
 }
 Start-Sleep -Milliseconds 1000
}
Write-Host 'Observation completed.'
