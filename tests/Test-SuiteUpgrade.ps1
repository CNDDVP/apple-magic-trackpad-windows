[CmdletBinding()]
param([string]$PackageRoot)
$ErrorActionPreference='Stop'
if(-not $PackageRoot){$PackageRoot=Split-Path (Split-Path $PSCommandPath -Parent) -Parent}
. (Join-Path $PackageRoot 'Suite_Integration.ps1')
$sandbox=Join-Path $env:TEMP ('MagicTrackpad-suite-test-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $sandbox|Out-Null
$runtime=Join-Path $sandbox 'runtime'
New-Item -ItemType Directory -Path $runtime|Out-Null
Copy-Item -LiteralPath (Join-Path $PackageRoot 'MagicForceClick.exe') -Destination $runtime
$config=Join-Path $runtime 'MagicForceClick.json'
$text=[IO.File]::ReadAllText((Join-Path $PackageRoot 'MagicForceClick.json')).Trim()
# An existing explicit opt-in must survive an upgrade from the new opt-out default.
$text=[regex]::Replace($text,'("AccelerateWhilePressed"\s*:\s*)false','$1true')
$text=$text.Substring(0,$text.Length-1)+',"FutureValue":{"Keep":true}}'
[IO.File]::WriteAllText($config,$text,[Text.UTF8Encoding]::new($false))
$configHash=(Get-FileHash -LiteralPath $config).Hash
$backendHash=(Get-FileHash -LiteralPath (Join-Path $runtime 'MagicForceClick.exe')).Hash
$passed=0
function Assert($value,$message){if(-not $value){throw $message}}
$update=& (Join-Path $PackageRoot 'Update_MagicForceClick.ps1') -RuntimeDir $runtime -NoStartup -NoDesktopShortcut -DoNotStart
Assert ($update.Success -and $update.Version -eq (Get-Content -LiteralPath (Join-Path $PackageRoot 'RELEASE_VERSION') -Raw).Trim()) 'Suite version incorrect'
Assert (Test-Path -LiteralPath (Join-Path $runtime 'MagicControlCenter\MagicControlCenter.exe.config')) 'DPI config missing'
Assert (Test-Path -LiteralPath (Join-Path $runtime 'Open_MagicTrackpad_ControlCenter.cmd')) 'Control center entry missing'
$passed++;'PASS v1.1.1 upgrade includes control center, DPI config and launcher'
Assert ((Get-FileHash -LiteralPath $config).Hash -eq $configHash) 'Personal JSON changed'
Assert ((Get-FileHash -LiteralPath (Join-Path $runtime 'MagicForceClick.exe')).Hash -eq $backendHash) 'Validated backend changed'
$passed++;'PASS personal configuration and validated backend bytes preserved'
& (Join-Path $update.SnapshotDir 'Restore_MagicForceClick.ps1') -SnapshotDir $update.SnapshotDir -DoNotStart|Out-Null
Assert (-not(Test-Path -LiteralPath (Join-Path $runtime 'MagicControlCenter\MagicControlCenter.exe'))) 'New control center remained'
Assert ((Get-FileHash -LiteralPath $config).Hash -eq $configHash) 'Rollback changed original JSON'
$passed++;'PASS standalone rollback removes new GUI files and restores original bytes'
New-Item -ItemType Directory -Path (Join-Path $runtime 'MagicControlCenter') -Force|Out-Null
[IO.File]::WriteAllText((Join-Path $runtime 'MagicControlCenter\MagicControlCenter.exe'),'old GUI fixture')
[IO.File]::WriteAllText((Join-Path $runtime 'MagicControlCenter\UserNotes.txt'),'keep user notes')
$oldGui=(Get-FileHash -LiteralPath (Join-Path $runtime 'MagicControlCenter\MagicControlCenter.exe')).Hash
$update=& (Join-Path $PackageRoot 'Update_MagicForceClick.ps1') -RuntimeDir $runtime -NoStartup -NoDesktopShortcut -DoNotStart
& (Join-Path $update.SnapshotDir 'Restore_MagicForceClick.ps1') -SnapshotDir $update.SnapshotDir -DoNotStart|Out-Null
Assert ((Get-FileHash -LiteralPath (Join-Path $runtime 'MagicControlCenter\MagicControlCenter.exe')).Hash -eq $oldGui) 'Existing GUI not restored'
Assert (Test-Path -LiteralPath (Join-Path $runtime 'MagicControlCenter\UserNotes.txt')) 'Unmanaged file removed'
$passed++;'PASS existing GUI restored and unmanaged files preserved'
$update=& (Join-Path $PackageRoot 'Update_MagicForceClick.ps1') -RuntimeDir $runtime -NoStartup -NoDesktopShortcut -DoNotStart
$savedGui=Join-Path $update.SnapshotDir 'files\MagicControlCenter\MagicControlCenter.exe'
[IO.File]::AppendAllText($savedGui,'tampered')
$before=(Get-FileHash -LiteralPath (Join-Path $runtime 'MagicControlCenter\MagicControlCenter.exe')).Hash
$rejected=$false;try{& (Join-Path $update.SnapshotDir 'Restore_MagicForceClick.ps1') -SnapshotDir $update.SnapshotDir -DoNotStart|Out-Null}catch{$rejected=$true}
Assert $rejected 'Corrupt GUI snapshot accepted'
Assert ((Get-FileHash -LiteralPath (Join-Path $runtime 'MagicControlCenter\MagicControlCenter.exe')).Hash -eq $before) 'Corrupt restore mutated GUI'
$passed++;'PASS corrupt GUI snapshot rejected before changes'
$rejected=$false;try{& (Join-Path $PackageRoot 'Update_MagicForceClick.ps1') -RuntimeDir $PackageRoot -NoStartup -NoDesktopShortcut -DoNotStart|Out-Null}catch{$rejected=$true}
Assert $rejected 'In-place update accepted'
$passed++;'PASS source/runtime path collision rejected'
$legacy=Join-Path $sandbox 'legacy'
New-Item -ItemType Directory -Path (Join-Path $legacy 'files') -Force|Out-Null
[IO.File]::WriteAllText((Join-Path $legacy 'files\old.txt'),'legacy')
[pscustomobject]@{SchemaVersion=1;RuntimeDir=$runtime;WasRunning=$false;StartupCaptured=$false;Files=@(@{Path='old.txt';Existed=$true;SHA256=(Get-FileHash -LiteralPath (Join-Path $legacy 'files\old.txt')).Hash})}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $legacy 'Snapshot.json') -Encoding UTF8
& (Join-Path $PackageRoot 'Restore_MagicForceClick.ps1') -SnapshotDir $legacy -DoNotStart|Out-Null
Assert ((Get-Content -LiteralPath (Join-Path $runtime 'old.txt') -Raw) -eq 'legacy') 'Legacy snapshot unsupported'
$passed++;'PASS legacy schema-1 snapshots remain restorable'
"RESULT suite_upgrade_passed=$passed failed=0"
