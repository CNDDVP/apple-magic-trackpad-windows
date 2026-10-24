[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$SnapshotDir,[switch]$DoNotStart)
$ErrorActionPreference='Stop'
$SnapshotDir=[IO.Path]::GetFullPath($SnapshotDir)
$manifest=Get-Content -LiteralPath (Join-Path $SnapshotDir 'Snapshot.json') -Raw -Encoding UTF8|ConvertFrom-Json
if($manifest.SchemaVersion -notin @(1,2)){throw 'Unknown snapshot schema.'}
$runtime=[IO.Path]::GetFullPath($manifest.RuntimeDir)
$prefix=$runtime.TrimEnd('\')+'\'
$sourcePrefix=(Join-Path $SnapshotDir 'files').TrimEnd('\')+'\'
foreach($entry in $manifest.Files){
 $target=[IO.Path]::GetFullPath((Join-Path $runtime $entry.Path))
 $source=[IO.Path]::GetFullPath((Join-Path $sourcePrefix $entry.Path))
 if(-not $target.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or -not $source.StartsWith($sourcePrefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Snapshot path escapes its directory.'}
 if($entry.Existed -and (Get-FileHash -LiteralPath $source).Hash -ne $entry.SHA256){throw "Snapshot checksum mismatch: $($entry.Path)"}
}
$shortcut=if($manifest.SchemaVersion -eq 2){$manifest.DesktopShortcut}else{$null}
if($shortcut){
 $expected=Join-Path ([Environment]::GetFolderPath('Desktop')) '妙控板控制中心.lnk'
 if($shortcut.Path -ne $expected){throw 'Unexpected desktop shortcut restore path.'}
 if($shortcut.Existed -and (Get-FileHash -LiteralPath (Join-Path $SnapshotDir 'DesktopShortcut.lnk')).Hash -ne $shortcut.SHA256){throw 'Shortcut backup checksum mismatch.'}
}
$gui=Join-Path $runtime 'MagicControlCenter\MagicControlCenter.exe'
foreach($old in @(Get-Process -Name MagicControlCenter -ErrorAction SilentlyContinue|Where-Object{$_.Path -eq $gui})){
 if(-not $old.CloseMainWindow() -or -not $old.WaitForExit(5000)){throw 'Close the control center before restoring.'}
}
$exe=Join-Path $runtime 'MagicForceClick.exe'
foreach($old in @(Get-Process -Name MagicForceClick -ErrorAction SilentlyContinue|Where-Object{$_.Path -eq $exe})){
 $old.Kill()
 if(-not $old.WaitForExit(10000)){throw 'Current runtime did not exit.'}
 $old.Dispose()
}
foreach($entry in $manifest.Files){
 $target=Join-Path $runtime $entry.Path
 if($entry.Existed){
  New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force|Out-Null
  [IO.File]::Copy((Join-Path $sourcePrefix $entry.Path),$target,$true)
  if((Get-FileHash -LiteralPath $target).Hash -ne $entry.SHA256){throw "Restore verification failed: $($entry.Path)"}
 }elseif(Test-Path -LiteralPath $target -PathType Leaf){Remove-Item -LiteralPath $target -Force}
}
if($shortcut){
 if($shortcut.Existed){Copy-Item -LiteralPath (Join-Path $SnapshotDir 'DesktopShortcut.lnk') -Destination $shortcut.Path -Force}
 elseif(Test-Path -LiteralPath $shortcut.Path -PathType Leaf){Remove-Item -LiteralPath $shortcut.Path -Force}
}
if($manifest.StartupCaptured){
 $key='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
 if($null -ne $manifest.StartupValue){New-ItemProperty -LiteralPath $key -Name MagicForceClick -Value $manifest.StartupValue -PropertyType String -Force|Out-Null}
 else{Remove-ItemProperty -LiteralPath $key -Name MagicForceClick -ErrorAction SilentlyContinue}
}
if($manifest.WasRunning -and -not $DoNotStart){
 $runtimeArguments=if($manifest.StartupValue -match '^"[^"]+"\s+(.+)$'){$Matches[1]}else{$null}
 if($runtimeArguments){Start-Process -FilePath $exe -ArgumentList $runtimeArguments -WindowStyle Hidden}else{Start-Process -FilePath $exe -WindowStyle Hidden}
}
[pscustomobject]@{Restored=$true;RuntimeDir=$runtime;Snapshot=$SnapshotDir;ExeSHA256=if(Test-Path -LiteralPath $exe){(Get-FileHash -LiteralPath $exe).Hash}else{$null}}
