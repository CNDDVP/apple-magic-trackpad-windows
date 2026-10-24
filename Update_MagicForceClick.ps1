[CmdletBinding()]
param([string]$RuntimeDir,[switch]$NoStartup,[switch]$DoNotStart,[switch]$NoDesktopShortcut)
$ErrorActionPreference='Stop'
$source=$PSScriptRoot
. (Join-Path $source 'Suite_Integration.ps1')
$runKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if(-not $RuntimeDir){
 $command=(Get-ItemProperty -LiteralPath $runKey -ErrorAction SilentlyContinue).MagicForceClick
 if($command -match '^"([^"]+\.exe)"'){$RuntimeDir=Split-Path $Matches[1] -Parent}
 else{$RuntimeDir=Join-Path $env:LOCALAPPDATA 'Programs\MagicTrackpad'}
}
$RuntimeDir=[IO.Path]::GetFullPath($RuntimeDir).TrimEnd('\')
if($RuntimeDir -eq $source.TrimEnd('\')){throw 'Extract the update separately from the installed runtime.'}
$files=@(Get-MagicTrackpadSuiteFiles)
foreach($file in $files){if(-not(Test-Path -LiteralPath (Join-Path $source $file) -PathType Leaf)){throw "Missing update file: $file"}}
$manifestPath=Join-Path $source 'SHA256SUMS-update.json'
if(-not(Test-Path -LiteralPath $manifestPath)){$manifestPath=Join-Path $source 'SHA256SUMS.json'}
$checks=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8|ConvertFrom-Json
foreach($file in $files){
 $record=@($checks|Where-Object{$_.Path.Replace('/','\') -eq $file})
 if($record.Count -ne 1 -or (Get-FileHash -LiteralPath (Join-Path $source $file)).Hash -ne $record[0].SHA256){throw "Update checksum mismatch: $file"}
}
$assembly=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $source 'MagicForceClick.exe')))
$optionsType=$assembly.GetType('MagicForceClick.ForceClickOptions',$true)
$backendVersion=$assembly.GetName().Version.ToString(3)
$config=Join-Path $RuntimeDir 'MagicForceClick.json'
$configToCheck=if(Test-Path -LiteralPath $config){$config}else{Join-Path $source 'MagicForceClick.json'}
[void]$optionsType.GetMethod('Load').Invoke($null,[object[]]@([string]$configToCheck))
$oldConfigHash=if(Test-Path -LiteralPath $config){(Get-FileHash -LiteralPath $config).Hash}else{$null}
$gui=Join-Path $RuntimeDir 'MagicControlCenter\MagicControlCenter.exe'
$guiChanged=-not(Test-Path -LiteralPath $gui) -or (Get-FileHash -LiteralPath $gui).Hash -ne (Get-FileHash -LiteralPath (Join-Path $source 'MagicControlCenter\MagicControlCenter.exe')).Hash
if($guiChanged){Close-MagicTrackpadControlCenter $RuntimeDir}
$snapshot=Join-Path $RuntimeDir ('rollback-snapshots\'+(Get-Date -Format yyyyMMdd_HHmmss)+'_'+[guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path (Join-Path $snapshot 'files') -Force|Out-Null
$exe=Join-Path $RuntimeDir 'MagicForceClick.exe'
$running=@(Get-Process -Name MagicForceClick -ErrorAction SilentlyContinue|Where-Object{$_.Path -eq $exe})
$backendChanged=-not(Test-Path -LiteralPath $exe) -or (Get-FileHash -LiteralPath $exe).Hash -ne (Get-FileHash -LiteralPath (Join-Path $source 'MagicForceClick.exe')).Hash
$entries=@(foreach($file in ($files+@('MagicForceClick.status.json','MagicForceClick.log'))){
 $path=Join-Path $RuntimeDir $file
 $exists=Test-Path -LiteralPath $path -PathType Leaf
 $saved=Join-Path (Join-Path $snapshot 'files') $file
 if($exists){New-Item -ItemType Directory -Path (Split-Path $saved -Parent) -Force|Out-Null;Copy-Item -LiteralPath $path -Destination $saved}
 [pscustomobject]@{Path=$file;Existed=$exists;SHA256=if($exists){(Get-FileHash -LiteralPath $saved).Hash}else{$null}}
})
$shortcut=$null
if(-not $NoDesktopShortcut){
 $shortcutPath=Get-MagicTrackpadShortcutPath
 $shortcutExists=Test-Path -LiteralPath $shortcutPath -PathType Leaf
 if($shortcutExists){Copy-Item -LiteralPath $shortcutPath -Destination (Join-Path $snapshot 'DesktopShortcut.lnk')}
 $shortcut=[pscustomobject]@{Path=$shortcutPath;Existed=$shortcutExists;SHA256=if($shortcutExists){(Get-FileHash -LiteralPath $shortcutPath).Hash}else{$null}}
}
$oldRun=(Get-ItemProperty -LiteralPath $runKey -ErrorAction SilentlyContinue).MagicForceClick
[pscustomobject]@{SchemaVersion=2;Time=(Get-Date -Format o);RuntimeDir=$RuntimeDir;WasRunning=($running.Count -gt 0);StartupCaptured=(-not $NoStartup);StartupValue=$oldRun;DesktopShortcut=$shortcut;Files=$entries}|
 ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $snapshot 'Snapshot.json') -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $source 'Restore_MagicForceClick.ps1') -Destination (Join-Path $snapshot 'Restore_MagicForceClick.ps1')
try{
 if($backendChanged){foreach($old in $running){$old.Kill();if(-not $old.WaitForExit(10000)){throw 'Old runtime did not exit.'};$old.Dispose()}}
 foreach($file in $files){
  $target=Join-Path $RuntimeDir $file
  if($file -eq 'MagicForceClick.json' -and (Test-Path -LiteralPath $target)){continue}
  $wanted=(Get-FileHash -LiteralPath (Join-Path $source $file)).Hash
  if((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target).Hash -eq $wanted){continue}
  New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force|Out-Null
  [IO.File]::Copy((Join-Path $source $file),$target,$true)
  if((Get-FileHash -LiteralPath $target).Hash -ne $wanted){throw "Copy verification failed: $file"}
 }
 if($oldConfigHash -and (Get-FileHash -LiteralPath $config).Hash -ne $oldConfigHash){throw 'Existing configuration changed during update.'}
 if(-not $NoStartup){New-ItemProperty -LiteralPath $runKey -Name MagicForceClick -Value ('"'+$exe+'"') -PropertyType String -Force|Out-Null}
 if(-not $NoDesktopShortcut){New-MagicTrackpadShortcut $RuntimeDir}
 if(-not $DoNotStart){
  if(-not(Get-Process -Name MagicForceClick -ErrorAction SilentlyContinue|Where-Object{$_.Path -eq $exe})){Start-Process -FilePath $exe -WindowStyle Hidden}
  $deadline=(Get-Date).AddSeconds(12)
  do{
   Start-Sleep -Milliseconds 250
   $process=Get-Process -Name MagicForceClick -ErrorAction SilentlyContinue|Where-Object{$_.Path -eq $exe}
   $statusPath=Join-Path $RuntimeDir 'MagicForceClick.status.json'
   $status=if(Test-Path -LiteralPath $statusPath){try{Get-Content -LiteralPath $statusPath -Raw|ConvertFrom-Json}catch{$null}}else{$null}
   $fresh=(Test-Path -LiteralPath $statusPath) -and ((Get-Date).ToUniversalTime()-(Get-Item -LiteralPath $statusPath).LastWriteTimeUtc).TotalSeconds -lt 15
  }until(($process -and $fresh -and $status.Version -eq $backendVersion -and $status.RawInputRegistered) -or (Get-Date) -gt $deadline)
  if(-not($process -and $fresh -and $status.Version -eq $backendVersion -and $status.RawInputRegistered)){throw 'Runtime startup verification failed.'}
 }
 [pscustomobject]@{Success=$true;Version=(Get-Content -LiteralPath (Join-Path $source 'RELEASE_VERSION') -Raw).Trim();BackendVersion=$backendVersion;ControlCenter=$gui;RuntimeDir=$RuntimeDir;SnapshotDir=$snapshot;SnapshotManifest=(Get-FileHash -LiteralPath (Join-Path $snapshot 'Snapshot.json')).Hash}
}catch{
 $failure=$_.Exception.Message
 try{& (Join-Path $snapshot 'Restore_MagicForceClick.ps1') -SnapshotDir $snapshot -DoNotStart:$DoNotStart|Out-Null}
 catch{throw "Update failed: $failure. Recovery also failed: $($_.Exception.Message). Keep snapshot: $snapshot"}
 throw "Update failed; previous files and startup restored: $failure"
}
