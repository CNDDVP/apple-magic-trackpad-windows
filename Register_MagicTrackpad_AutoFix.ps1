#Requires -RunAsAdministrator
[CmdletBinding()]
param([string]$RuntimeDir=$PSScriptRoot)
$ErrorActionPreference='Stop'
$vbs=Join-Path $RuntimeDir 'MagicTrackpad_AutoFix_Silent.vbs'
if (-not (Test-Path -LiteralPath $vbs)) { throw "Missing task launcher: $vbs" }
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$action=New-ScheduledTaskAction -Execute "$env:WINDIR\System32\wscript.exe" -Argument "`"$vbs`""
$logon=New-ScheduledTaskTrigger -AtLogOn -User $identity.Name
$repeat=New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 15)
$eventClass=Get-CimClass -Namespace Root/Microsoft/Windows/TaskScheduler -ClassName MSFT_TaskEventTrigger
$wake=New-CimInstance -ClientOnly -CimClass $eventClass -Property @{
 Enabled=$true
 Subscription="<QueryList><Query Id='0' Path='System'><Select Path='System'>*[System[Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and EventID=1]]</Select></Query></QueryList>"
}
$settings=New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -Hidden -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 2)
$principal=New-ScheduledTaskPrincipal -UserId $identity.Name -LogonType Interactive -RunLevel Highest
Register-ScheduledTask -TaskName MagicTrackpad_AutoFix -TaskPath '\' -Action $action -Trigger @($logon,$repeat,$wake) -Settings $settings -Principal $principal -Force | Out-Null
$task=Get-ScheduledTask -TaskName MagicTrackpad_AutoFix
if (@($task.Triggers | Where-Object {$_.CimClass.CimClassName -eq 'MSFT_TaskEventTrigger'}).Count -ne 1) { throw 'Wake trigger verification failed.' }
[pscustomobject]@{Task=$task.TaskName;Triggers=@($task.Triggers | ForEach-Object {$_.CimClass.CimClassName});RuntimeDir=$RuntimeDir}
