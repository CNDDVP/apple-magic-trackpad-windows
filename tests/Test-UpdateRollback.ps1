$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$root=Join-Path $env:TEMP ('MagicTrackpad-rollback-test-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$baseline=Join-Path $repo 'artifacts\baseline\MagicForceClick.exe'
if(-not(Test-Path -LiteralPath $baseline)){throw 'Place the pre-upgrade executable at artifacts\baseline\MagicForceClick.exe.'}
$baselineHash=(Get-FileHash -LiteralPath $baseline).Hash
$passed=0
function Assert([bool]$value,[string]$text){if(-not $value){throw $text}}
$fixture=Join-Path $root 'runtime'
New-Item -ItemType Directory -Path $fixture | Out-Null
Copy-Item -LiteralPath $baseline -Destination (Join-Path $fixture 'MagicForceClick.exe')
'old source' | Set-Content -LiteralPath (Join-Path $fixture 'MagicForceClick.cs')
$update=& (Join-Path $repo 'Update_MagicForceClick.ps1') -RuntimeDir $fixture -NoStartup -DoNotStart
Assert ($update.Success -and (Get-FileHash -LiteralPath (Join-Path $fixture 'MagicForceClick.exe')).Hash -ne $baselineHash) 'Update did not replace the executable'
$passed++; 'PASS isolated update'
& (Join-Path $repo 'Restore_MagicForceClick.ps1') -SnapshotDir $update.SnapshotDir -DoNotStart | Out-Null
Assert ((Get-FileHash -LiteralPath (Join-Path $fixture 'MagicForceClick.exe')).Hash -eq $baselineHash) 'Baseline executable was not restored exactly'
Assert (-not(Test-Path -LiteralPath (Join-Path $fixture 'MagicForceClick.Core.cs'))) 'New file remained after rollback'
Assert (-not(Test-Path -LiteralPath (Join-Path $fixture 'MagicForceClick.Preview.cs'))) 'New Opus preview file remained after rollback'
Assert (-not(Test-Path -LiteralPath (Join-Path $fixture 'MagicForceClick.json'))) 'New configuration remained after rollback'
$passed++; 'PASS rollback restores exact hash and removes new files'
$saved=Join-Path $update.SnapshotDir 'files\MagicForceClick.exe'
[IO.File]::WriteAllText($saved,'tampered')
$rejected=$false
try{& (Join-Path $repo 'Restore_MagicForceClick.ps1') -SnapshotDir $update.SnapshotDir -DoNotStart | Out-Null}catch{$rejected=$true}
Assert $rejected 'Corrupt snapshot was accepted'
Assert ((Get-FileHash -LiteralPath (Join-Path $fixture 'MagicForceClick.exe')).Hash -eq $baselineHash) 'Corrupt snapshot changed runtime'
$passed++; 'PASS corrupted snapshot rejected before changes'
'{invalid' | Set-Content -LiteralPath (Join-Path $fixture 'MagicForceClick.json')
$rejected=$false
try{& (Join-Path $repo 'Update_MagicForceClick.ps1') -RuntimeDir $fixture -NoStartup -DoNotStart | Out-Null}catch{$rejected=$true}
Assert $rejected 'Invalid existing configuration was accepted'
Assert ((Get-FileHash -LiteralPath (Join-Path $fixture 'MagicForceClick.exe')).Hash -eq $baselineHash) 'Invalid configuration update changed runtime'
Remove-Item -LiteralPath (Join-Path $fixture 'MagicForceClick.json')
$passed++; 'PASS invalid configuration aborts before mutation'
# Existing production daemon holds the singleton mutex. A separate fixture startup must fail safely.
if(Get-Process -Name MagicForceClick -ErrorAction SilentlyContinue){
 $failed=$false
 try{& (Join-Path $repo 'Update_MagicForceClick.ps1') -RuntimeDir $fixture -NoStartup | Out-Null}catch{$failed=$true}
 Assert $failed 'Expected fixture startup to be rejected by existing singleton'
 Assert ((Get-FileHash -LiteralPath (Join-Path $fixture 'MagicForceClick.exe')).Hash -eq $baselineHash) 'Failed startup did not auto-restore baseline'
 $passed++; 'PASS failed startup automatically rolls back'
}
"RESULT update_rollback_passed=$passed"
