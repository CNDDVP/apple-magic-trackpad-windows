[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$BuildDirectory,
      [string]$OutputRoot=(Join-Path $PSScriptRoot '..\artifacts\control-center-ui'))
$ErrorActionPreference='Stop'
if(Get-Process -Name 'MagicControlCenter.UiTests' -ErrorAction SilentlyContinue){throw 'Another UI acceptance run is active.'}
$build=Get-Content -LiteralPath (Join-Path $BuildDirectory 'ControlCenterBuild.json') -Raw|ConvertFrom-Json
if($build.UnitResults -notmatch 'failed=0$'){throw 'Core acceptance did not pass.'}
$output=Join-Path ([IO.Path]::GetFullPath($OutputRoot)) ('ui-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $output -Force|Out-Null
$source=Join-Path $BuildDirectory 'MagicControlCenter.UiTests.exe'
$target=Join-Path $output 'MagicControlCenter.UiTests.exe'
Copy-Item -LiteralPath $source -Destination $target
Copy-Item -LiteralPath ($source+'.config') -Destination ($target+'.config')
$report=[ordered]@{Started=(Get-Date -Format o);Output=$output;AppSha256=$build.AppSha256;UiTestSha256=(Get-FileHash -LiteralPath $target).Hash;ExitCode=$null;Passed=$false}
if($report.UiTestSha256 -ne $build.UiTestSha256){throw 'UI test bytes do not match the build record.'}
$report|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $output 'Run.json') -Encoding UTF8
$process=Start-Process -FilePath $target -ArgumentList ('"'+$output+'"') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $output 'Console.txt') -RedirectStandardError (Join-Path $output 'Errors.txt')
$null=$process.Handle
if(-not $process.WaitForExit(45000)){
    Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
    throw "UI acceptance timed out: $output"
}
$process.WaitForExit()
$lines=Get-Content -LiteralPath (Join-Path $output 'UiResults.txt')
$expectedLine=$lines|Where-Object{$_ -match '^expected=\d+$'}
$expected=if($expectedLine){[int]($expectedLine -replace '^expected=','')}else{0}
$report.ExitCode=$process.ExitCode
$report.Passed=$process.ExitCode -eq 0 -and $expected -gt 0 -and ($lines -contains "control_center_ui_passed=$expected") -and ($lines -contains 'failed=0') -and ($lines -contains 'completed=true')
$report.Finished=Get-Date -Format o
$report|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $output 'Run.json') -Encoding UTF8
$output|Set-Content -LiteralPath (Join-Path $OutputRoot 'Latest.txt') -Encoding UTF8
Get-Content -LiteralPath (Join-Path $output 'UiTestLog.txt')
Write-Output "OUTPUT=$output"
if(-not $report.Passed){throw 'Interactive UI acceptance failed.'}
