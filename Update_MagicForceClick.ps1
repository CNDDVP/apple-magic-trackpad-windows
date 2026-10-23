[CmdletBinding()]
param([string]$RuntimeDir,[switch]$NoStartup,[switch]$DoNotStart)
$ErrorActionPreference='Stop'
$source=$PSScriptRoot
$runKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if(-not $RuntimeDir){
 $command=(Get-ItemProperty -LiteralPath $runKey -ErrorAction SilentlyContinue).MagicForceClick
 if($command -match '^"([^"]+\.exe)"'){$RuntimeDir=Split-Path $Matches[1] -Parent}
 else{$RuntimeDir=Join-Path $env:LOCALAPPDATA 'Programs\MagicTrackpad'}
}
$RuntimeDir=[IO.Path]::GetFullPath($RuntimeDir)
$files=@('MagicForceClick.exe','MagicForceClick.cs','MagicForceClick.Core.cs','MagicForceClick.Settings.cs','MagicForceClick.json','Open_MagicTrackpad_Settings.cmd','Restore_MagicForceClick.ps1','Update_MagicForceClick.ps1')
foreach($file in $files){if(-not(Test-Path -LiteralPath (Join-Path $source $file))){throw "Missing update file: $file"}}
$newExe=Join-Path $source 'MagicForceClick.exe'
$assembly=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes($newExe))
$optionsType=$assembly.GetType('MagicForceClick.ForceClickOptions',$true)
$existingConfig=Join-Path $RuntimeDir 'MagicForceClick.json'
$configToCheck=if(Test-Path -LiteralPath $existingConfig){$existingConfig}else{Join-Path $source 'MagicForceClick.json'}
[void]$optionsType.GetMethod('Load').Invoke($null,[object[]]@([string]$configToCheck))
$snapshot=Join-Path $RuntimeDir ('rollback-snapshots\'+(Get-Date -Format yyyyMMdd_HHmmss)+'_'+[guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path (Join-Path $snapshot 'files') -Force | Out-Null
$exe=Join-Path $RuntimeDir 'MagicForceClick.exe'
$running=@(Get-Process -Name MagicForceClick -ErrorAction SilentlyContinue | Where-Object{$_.Path -eq $exe})
$entries=@(foreach($file in ($files+@('MagicForceClick.status.json','MagicForceClick.log'))){
 $path=Join-Path $RuntimeDir $file
 $exists=Test-Path -LiteralPath $path
 if($exists){Copy-Item -LiteralPath $path -Destination (Join-Path $snapshot ('files\'+$file))}
 [pscustomobject]@{Path=$file;Existed=$exists;SHA256=if($exists){(Get-FileHash -LiteralPath (Join-Path $snapshot ('files\'+$file))).Hash}else{$null}}
})
$oldRun=(Get-ItemProperty -LiteralPath $runKey -ErrorAction SilentlyContinue).MagicForceClick
[pscustomobject]@{SchemaVersion=1;Time=(Get-Date -Format o);RuntimeDir=$RuntimeDir;WasRunning=($running.Count -gt 0);StartupCaptured=(-not $NoStartup);StartupValue=$oldRun;Files=$entries} |
 ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $snapshot 'Snapshot.json') -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $source 'Restore_MagicForceClick.ps1') -Destination (Join-Path $snapshot 'Restore_MagicForceClick.ps1')
try {
 foreach($old in $running){
  Write-Verbose ("Stopping runtime PID="+$old.Id)
  $old.Kill()
  if(-not $old.WaitForExit(10000)){throw 'Old runtime did not exit.'}
  $old.Dispose()
 }
 foreach($file in $files){
  if($file -eq 'MagicForceClick.json' -and (Test-Path -LiteralPath (Join-Path $RuntimeDir $file))){continue}
  for($attempt=0;$attempt -lt 15;$attempt++){
   try{[IO.File]::Copy((Join-Path $source $file),(Join-Path $RuntimeDir $file),$true);break}
   catch{if($attempt -eq 14){throw};Start-Sleep -Milliseconds 100}
  }
  if((Get-FileHash -LiteralPath (Join-Path $RuntimeDir $file)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $source $file)).Hash){throw "Copy verification failed: $file"}
 }
 if(-not $NoStartup){New-ItemProperty -LiteralPath $runKey -Name MagicForceClick -Value ('"'+$exe+'"') -PropertyType String -Force | Out-Null}
 if(-not $DoNotStart){
  Start-Process -FilePath $exe -WindowStyle Hidden
  $deadline=(Get-Date).AddSeconds(10)
  do{
   Start-Sleep -Milliseconds 250
   $process=Get-Process -Name MagicForceClick -ErrorAction SilentlyContinue | Where-Object{$_.Path -eq $exe}
   $statusPath=Join-Path $RuntimeDir 'MagicForceClick.status.json'
   $status=if(Test-Path -LiteralPath $statusPath){try{Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json}catch{$null}}else{$null}
  }until(($process -and $status.Version -eq '1.1.0' -and $status.RawInputRegistered) -or (Get-Date) -gt $deadline)
  if(-not($process -and $status.Version -eq '1.1.0' -and $status.RawInputRegistered)){throw 'Runtime startup verification failed.'}
 }
 [pscustomobject]@{Success=$true;Version='1.1.0';RuntimeDir=$RuntimeDir;SnapshotDir=$snapshot;SnapshotManifest=(Get-FileHash -LiteralPath (Join-Path $snapshot 'Snapshot.json')).Hash}
}catch{
 $failure=$_.Exception.Message+' at '+$_.ScriptStackTrace
 & (Join-Path $snapshot 'Restore_MagicForceClick.ps1') -SnapshotDir $snapshot -DoNotStart:$DoNotStart | Out-Null
 throw "Update failed and previous files/startup were restored: $failure"
}
