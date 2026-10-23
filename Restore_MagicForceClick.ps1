[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$SnapshotDir,[switch]$DoNotStart)
$ErrorActionPreference='Stop'
$SnapshotDir=[IO.Path]::GetFullPath($SnapshotDir)
$manifest=Get-Content -LiteralPath (Join-Path $SnapshotDir 'Snapshot.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if($manifest.SchemaVersion -ne 1){throw 'Unknown snapshot schema.'}
$runtime=[IO.Path]::GetFullPath($manifest.RuntimeDir)
$prefix=$runtime.TrimEnd('\')+'\'
$sourcePrefix=(Join-Path $SnapshotDir 'files').TrimEnd('\')+'\'
foreach($entry in $manifest.Files){
 $target=[IO.Path]::GetFullPath((Join-Path $runtime $entry.Path))
 $source=[IO.Path]::GetFullPath((Join-Path $sourcePrefix $entry.Path))
 if(-not $target.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or -not $source.StartsWith($sourcePrefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Snapshot path escapes its directory.'}
 if($entry.Existed -and (Get-FileHash -LiteralPath $source).Hash -ne $entry.SHA256){throw "Snapshot checksum mismatch: $($entry.Path)"}
}
$exe=Join-Path $runtime 'MagicForceClick.exe'
foreach($old in @(Get-Process -Name MagicForceClick -ErrorAction SilentlyContinue | Where-Object{$_.Path -eq $exe})){
 $old.Kill()
 if(-not $old.WaitForExit(10000)){throw 'Current runtime did not exit.'}
 $old.Dispose()
}
foreach($entry in $manifest.Files){
 $target=Join-Path $runtime $entry.Path
 if($entry.Existed){
  for($attempt=0;$attempt -lt 15;$attempt++){
   try{[IO.File]::Copy((Join-Path $sourcePrefix $entry.Path),$target,$true);break}
   catch{if($attempt -eq 14){throw};Start-Sleep -Milliseconds 100}
  }
  if((Get-FileHash -LiteralPath $target).Hash -ne $entry.SHA256){throw "Restore verification failed: $($entry.Path)"}
 }elseif(Test-Path -LiteralPath $target){Remove-Item -LiteralPath $target -Force}
}
if($manifest.StartupCaptured){
 $key='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
 if($null -ne $manifest.StartupValue){New-ItemProperty -LiteralPath $key -Name MagicForceClick -Value $manifest.StartupValue -PropertyType String -Force | Out-Null}
 else{Remove-ItemProperty -LiteralPath $key -Name MagicForceClick -ErrorAction SilentlyContinue}
}
if($manifest.WasRunning -and -not $DoNotStart){
 $args=if($manifest.StartupValue -match '^"[^"]+"\s+(.+)$'){$Matches[1]}else{$null}
 if($args){Start-Process -FilePath $exe -ArgumentList $args -WindowStyle Hidden}else{Start-Process -FilePath $exe -WindowStyle Hidden}
}
[pscustomobject]@{Restored=$true;RuntimeDir=$runtime;Snapshot=$SnapshotDir;ExeSHA256=if(Test-Path -LiteralPath $exe){(Get-FileHash -LiteralPath $exe).Hash}else{$null}}
