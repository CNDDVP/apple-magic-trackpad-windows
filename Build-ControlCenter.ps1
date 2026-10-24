[CmdletBinding()]
param([string]$OutputDir=(Join-Path $PSScriptRoot 'artifacts'))
$ErrorActionPreference='Stop'
$OutputDir=[IO.Path]::GetFullPath($OutputDir)
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not(Test-Path -LiteralPath $csc)){throw '.NET Framework 4.x x64 compiler required.'}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$sources=@('MagicControlCenter.Assembly.cs','MagicForceClick.Core.cs','MagicControlCenter.Core.cs','MagicControlCenter.Platform.cs','MagicControlCenter.Window.cs')|ForEach-Object{Join-Path $PSScriptRoot $_}
$app=Join-Path $OutputDir 'MagicControlCenter.exe'
$manifest=Join-Path $PSScriptRoot 'MagicControlCenter.manifest'
$config=Join-Path $PSScriptRoot 'MagicControlCenter.exe.config'
& $csc /nologo /codepage:65001 /target:winexe /platform:anycpu /optimize+ "/win32manifest:$manifest" /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll /r:System.Web.Extensions.dll "/out:$app" $sources
if($LASTEXITCODE -ne 0){throw 'Control center build failed.'}
Copy-Item -LiteralPath $config -Destination ($app+'.config') -Force
$unit=Join-Path $OutputDir 'MagicControlCenter.Tests.exe'
& $csc /nologo /codepage:65001 /target:exe /platform:anycpu /optimize+ /r:System.dll /r:System.Runtime.Serialization.dll /r:System.Web.Extensions.dll "/out:$unit" (Join-Path $PSScriptRoot 'MagicForceClick.Core.cs') (Join-Path $PSScriptRoot 'MagicControlCenter.Core.cs') (Join-Path $PSScriptRoot 'MagicControlCenter.Platform.cs') (Join-Path $PSScriptRoot 'tests\MagicControlCenter.Tests.cs')
if($LASTEXITCODE -ne 0){throw 'Control center test build failed.'}
$unitResults=@(& $unit)
$unitResults | Set-Content -LiteralPath (Join-Path $OutputDir 'ControlCenterResults.txt') -Encoding UTF8
$unitResults | Write-Output
if($LASTEXITCODE -ne 0){throw 'Control center behavior tests failed.'}
$ui=Join-Path $OutputDir 'MagicControlCenter.UiTests.exe'
& $csc /nologo /codepage:65001 /target:exe /main:MagicControlCenter.Tests.WindowSmokeTests /platform:anycpu /optimize+ "/win32manifest:$manifest" /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll /r:System.Web.Extensions.dll "/out:$ui" $sources (Join-Path $PSScriptRoot 'tests\MagicControlCenter.UiTests.cs')
if($LASTEXITCODE -ne 0){throw 'Control center UI test build failed.'}
Copy-Item -LiteralPath $config -Destination ($ui+'.config') -Force
$live=Join-Path $OutputDir 'MagicControlCenter.LiveTests.exe'
& $csc /nologo /codepage:65001 /target:exe /main:MagicControlCenter.Tests.LiveTests /platform:anycpu /optimize+ "/win32manifest:$manifest" /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll /r:System.Web.Extensions.dll "/out:$live" $sources (Join-Path $PSScriptRoot 'tests\MagicControlCenter.LiveTests.cs')
if($LASTEXITCODE -ne 0){throw 'Control center live test build failed.'}
Copy-Item -LiteralPath $config -Destination ($live+'.config') -Force
# GUI checks need the real interactive Windows desktop, with its actual DPI.
# Never launch them from this non-interactive build session: two instances
# would also race over the same fixture and result directory.
[pscustomobject]@{Time=(Get-Date -Format o);App=$app;AppSha256=(Get-FileHash -LiteralPath $app).Hash;ConfigSha256=(Get-FileHash -LiteralPath ($app+'.config')).Hash;UnitResults=$unitResults[-1];UiTests=$ui;UiTestSha256=(Get-FileHash -LiteralPath $ui).Hash;LiveTests=$live;LiveTestSha256=(Get-FileHash -LiteralPath $live).Hash;InteractiveUiRequired=$true} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDir 'ControlCenterBuild.json') -Encoding UTF8
