[CmdletBinding(SupportsShouldProcess=$true)]
param([Parameter(Mandatory=$true)][string]$SnapshotDirectory)
$ErrorActionPreference='Stop'
$snapshot=Get-Content -LiteralPath (Join-Path $SnapshotDirectory 'Snapshot.json') -Raw|ConvertFrom-Json
$target=[IO.Path]::GetFullPath($snapshot.Target).TrimEnd('\')
if((Split-Path $target -Leaf) -ne 'MagicControlCenter'){throw 'Unexpected restore target.'}
function Resolve-Target([string]$Relative){
    $resolved=[IO.Path]::GetFullPath((Join-Path $target $Relative))
    if(-not $resolved.StartsWith($target+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Path escapes the control-center directory.'}
    return $resolved
}
foreach($file in $snapshot.OriginalFiles){
    $null=Resolve-Target $file.Path
    $source=Join-Path (Join-Path $SnapshotDirectory 'Previous') $file.Path
    if((Get-FileHash -LiteralPath $source).Hash -ne $file.Sha256){throw "Backup hash mismatch: $($file.Path)"}
}
foreach($relative in $snapshot.DeployedFiles){$null=Resolve-Target $relative}
if(-not $PSCmdlet.ShouldProcess($target,'Restore the previous control center; preserve the parent configuration')){return}
Get-Process -Name MagicControlCenter -ErrorAction SilentlyContinue|ForEach-Object{
    if([string]::Equals($_.Path,(Join-Path $target 'MagicControlCenter.exe'),[StringComparison]::OrdinalIgnoreCase)){
        if(-not $_.CloseMainWindow() -or -not $_.WaitForExit(5000)){throw 'Close the control center before restoring.'}
    }
}
foreach($file in $snapshot.OriginalFiles){
    $destination=Resolve-Target $file.Path
    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force|Out-Null
    Copy-Item -LiteralPath (Join-Path (Join-Path $SnapshotDirectory 'Previous') $file.Path) -Destination $destination -Force
}
$originalPaths=@($snapshot.OriginalFiles|ForEach-Object{$_.Path})
foreach($relative in $snapshot.DeployedFiles){
    if($relative -notin $originalPaths){$path=Resolve-Target $relative;if(Test-Path -LiteralPath $path -PathType Leaf){Remove-Item -LiteralPath $path -Force}}
}
foreach($file in $snapshot.OriginalFiles){if((Get-FileHash -LiteralPath (Resolve-Target $file.Path)).Hash -ne $file.Sha256){throw 'Restore readback failed.'}}
[pscustomobject]@{Restored=$true;Target=$target;ParentConfigurationChanged=$false}
