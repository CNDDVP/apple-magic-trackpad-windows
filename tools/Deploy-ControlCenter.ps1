[CmdletBinding(SupportsShouldProcess=$true)]
param([Parameter(Mandatory=$true)][string]$ProductionDirectory,
      [Parameter(Mandatory=$true)][string]$BuildDirectory,
      [Parameter(Mandatory=$true)][string]$AcceptanceDirectory)
$ErrorActionPreference='Stop'
$sourceRoot=Split-Path $PSScriptRoot -Parent
$production=[IO.Path]::GetFullPath($ProductionDirectory)
$target=Join-Path $production 'MagicControlCenter'
$config=Join-Path $production 'MagicForceClick.json'
if(-not(Test-Path -LiteralPath $config -PathType Leaf)){throw 'Production parent configuration is missing.'}
$configBefore=(Get-FileHash -LiteralPath $config).Hash
$build=Get-Content -LiteralPath (Join-Path $BuildDirectory 'ControlCenterBuild.json') -Raw|ConvertFrom-Json
$acceptance=Get-Content -LiteralPath (Join-Path $AcceptanceDirectory 'Run.json') -Raw|ConvertFrom-Json
$results=Get-Content -LiteralPath (Join-Path $AcceptanceDirectory 'UiResults.txt')
if(-not $acceptance.Passed -or $acceptance.ExitCode -ne 0 -or $results -notcontains 'completed=true' -or $results -notcontains 'failed=0'){throw 'Full interactive UI acceptance is required.'}
if($build.UnitResults -notmatch 'failed=0$'){throw 'Core tests did not pass.'}
$app=Join-Path $BuildDirectory 'MagicControlCenter.exe'
$appHash=(Get-FileHash -LiteralPath $app).Hash
if($appHash -ne $build.AppSha256 -or $appHash -ne $acceptance.AppSha256){throw 'Candidate bytes differ from the accepted build.'}
if((Get-FileHash -LiteralPath (Join-Path $AcceptanceDirectory 'MagicControlCenter.UiTests.exe')).Hash -ne $acceptance.UiTestSha256 -or $acceptance.UiTestSha256 -ne $build.UiTestSha256){throw 'UI test bytes differ from the accepted build.'}
if((Get-FileHash -LiteralPath ($app+'.config')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $AcceptanceDirectory 'MagicControlCenter.UiTests.exe.config')).Hash){throw 'Application DPI config differs from the tested config.'}
$fileMap=@{}
$fileMap['MagicControlCenter.exe']=$app
$fileMap['MagicControlCenter.exe.config']=$app+'.config'
$fileMap['MagicControlCenter.ico']=Join-Path $BuildDirectory 'MagicControlCenter.ico'
foreach($file in @('MagicControlCenter.Tests.exe','MagicControlCenter.LiveTests.exe','MagicControlCenter.LiveTests.exe.config','ControlCenterResults.txt','ControlCenterBuild.json')){$fileMap[$file]=Join-Path $BuildDirectory $file}
foreach($file in @('Run.json','UiResults.txt','UiTestLog.txt')){$fileMap['acceptance\'+$file]=Join-Path $AcceptanceDirectory $file}
foreach($file in @('MagicControlCenter.Assembly.cs','MagicForceClick.Core.cs','MagicControlCenter.Core.cs','MagicControlCenter.Platform.cs','MagicControlCenter.Window.cs','MagicControlCenter.manifest','Build-ControlCenter.ps1','MAGIC_CONTROL_CENTER.md','tests\MagicControlCenter.Tests.cs','tests\MagicControlCenter.UiTests.cs','tests\MagicControlCenter.LiveTests.cs','tools\Test-ControlCenter.ps1')){$fileMap[$file]=Join-Path $sourceRoot $file}
foreach($path in $fileMap.Values){if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw "Missing deployment input: $path"}}
if(-not $PSCmdlet.ShouldProcess($target,'Deploy the accepted standalone control center with an independent rollback snapshot')){return}
Get-Process -Name MagicControlCenter -ErrorAction SilentlyContinue|ForEach-Object{
    if([string]::Equals($_.Path,(Join-Path $target 'MagicControlCenter.exe'),[StringComparison]::OrdinalIgnoreCase)){
        if(-not $_.CloseMainWindow() -or -not $_.WaitForExit(5000)){throw 'The control center has unsaved changes; deployment stopped.'}
    }
}
$snapshotDir=Join-Path (Join-Path $production 'control-center-backups') ('deployment-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$previous=Join-Path $snapshotDir 'Previous'
New-Item -ItemType Directory -Path $previous -Force|Out-Null
$original=@()
if(Test-Path -LiteralPath $target){
    foreach($file in Get-ChildItem -LiteralPath $target -File -Recurse){
        $relative=$file.FullName.Substring($target.Length+1)
        $destination=Join-Path $previous $relative
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force|Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
        $original+=@{Path=$relative;Sha256=(Get-FileHash -LiteralPath $destination).Hash}
    }
}
$deployed=@($fileMap.Keys)+@('Deployment.json','打开妙控板控制中心.cmd')
$snapshot=@{Target=$target;OriginalFiles=$original;DeployedFiles=$deployed;ConfigHashBefore=$configBefore;Created=(Get-Date -Format o)}
$snapshot|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $snapshotDir 'Snapshot.json') -Encoding UTF8
Copy-Item -LiteralPath $config -Destination (Join-Path $snapshotDir 'MagicForceClick.before.json')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Restore-ControlCenter.ps1') -Destination $snapshotDir
$rollback="@echo off`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File `"%~dp0Restore-ControlCenter.ps1`" -SnapshotDirectory `"%~dp0.`"`r`npause`r`n"
[IO.File]::WriteAllText((Join-Path $snapshotDir 'Rollback.cmd'),$rollback,[Text.Encoding]::ASCII)
try {
    foreach($relative in $fileMap.Keys){
        $destination=Join-Path $target $relative
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force|Out-Null
        Copy-Item -LiteralPath $fileMap[$relative] -Destination $destination -Force
        if((Get-FileHash -LiteralPath $destination).Hash -ne (Get-FileHash -LiteralPath $fileMap[$relative]).Hash){throw "Deployment readback failed: $relative"}
    }
    [IO.File]::WriteAllText((Join-Path $target '打开妙控板控制中心.cmd'),"@echo off`r`nstart `"`" `"%~dp0MagicControlCenter.exe`"`r`n",[Text.Encoding]::ASCII)
    if((Get-FileHash -LiteralPath $config).Hash -ne $configBefore){throw 'Production JSON changed during deployment.'}
    $proof=[ordered]@{Time=(Get-Date -Format o);Deployed=$true;AppSha256=$appHash;ConfigSha256Before=$configBefore;ConfigSha256After=(Get-FileHash -LiteralPath $config).Hash;CoreAcceptance=$build.UnitResults;InteractiveUiAccepted=$true;UiAcceptanceDirectory=$AcceptanceDirectory;UiTestSha256=$acceptance.UiTestSha256;RollbackSnapshot=$snapshotDir;ProductionValidationPassed=$false}
    $proof|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $target 'Deployment.json') -Encoding UTF8
    [pscustomobject]$proof
} catch {
    & (Join-Path $snapshotDir 'Restore-ControlCenter.ps1') -SnapshotDirectory $snapshotDir -Confirm:$false|Out-Null
    throw
}
