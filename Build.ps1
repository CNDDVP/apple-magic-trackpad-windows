[CmdletBinding()]
param([string]$OutputDir=(Join-Path $PSScriptRoot 'artifacts'))
$ErrorActionPreference='Stop'
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not(Test-Path -LiteralPath $csc)){throw '.NET Framework 4.x x64 compiler required.'}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$sources=@('MagicForceClick.cs','MagicForceClick.Core.cs','MagicForceClick.Settings.cs')|ForEach-Object{Join-Path $PSScriptRoot $_}
$output=Join-Path $OutputDir 'MagicForceClick.exe'
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Runtime.Serialization.dll "/out:$output" $sources
if($LASTEXITCODE -ne 0){throw 'Runtime build failed.'}
$test=Join-Path $OutputDir 'MagicForceClick.Tests.exe'
& $csc /nologo /target:exe /platform:anycpu /optimize+ /r:System.Runtime.Serialization.dll "/out:$test" (Join-Path $PSScriptRoot 'MagicForceClick.Core.cs') (Join-Path $PSScriptRoot 'tests\MagicForceClick.Tests.cs')
if($LASTEXITCODE -ne 0){throw 'Test build failed.'}
$results=@(& $test)
$exit=$LASTEXITCODE
$results | Set-Content -LiteralPath (Join-Path $OutputDir 'TestResults.txt') -Encoding UTF8
$results | Write-Output
if($exit -ne 0){throw 'Behavior tests failed.'}
$integration=Join-Path $OutputDir 'MagicForceClick.IntegrationTests.exe'
& $csc /nologo /target:exe /main:MagicForceClick.Tests.IntegrationTests /platform:anycpu /optimize+ /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Runtime.Serialization.dll "/out:$integration" $sources (Join-Path $PSScriptRoot 'tests\MagicForceClick.IntegrationTests.cs')
if($LASTEXITCODE -ne 0){throw 'Integration test build failed.'}
$integrationResults=@(& $integration)
$integrationResults | Set-Content -LiteralPath (Join-Path $OutputDir 'IntegrationResults.txt') -Encoding UTF8
if($LASTEXITCODE -ne 0){throw 'Integration tests failed.'}
$integrationResults | Write-Output
[pscustomobject]@{Version='1.1.0';Time=(Get-Date -Format o);Summary=$results[-1];RuntimeSha256=(Get-FileHash -LiteralPath $output).Hash;Baseline='2594280052ed327c5a328f9b0e3d38f2ec7545ce'} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDir 'TestResults.json') -Encoding UTF8
