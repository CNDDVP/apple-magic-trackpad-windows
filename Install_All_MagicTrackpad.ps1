#Requires -RunAsAdministrator
<# Install for the currently signed-in user. Do not elevate using a different account.
Driver installation can require reconnection or reboot. Bluetooth pairing remains manual.
Realtek_Bluetooth_Backup is hardware-specific and is never installed automatically.
#>
[CmdletBinding()]
param(
 [string]$TargetDir="$env:LOCALAPPDATA\Programs\MagicTrackpad",
 [ValidateRange(120,250)][int]$ForceClickThreshold=180,
 [ValidateRange(1,20)][int]$CursorSpeed=14,
 [switch]$EnableThreeFingerDrag
)
$ErrorActionPreference='Stop'
$PackageRoot=$PSScriptRoot
$stamp=Get-Date -Format yyyyMMdd_HHmmss
$backup=Join-Path $env:LOCALAPPDATA "MagicTrackpad_Backups\$stamp"
New-Item -ItemType Directory -Path $backup -Force | Out-Null
Start-Transcript -LiteralPath (Join-Path $backup 'Install.log') | Out-Null
try {
 $osArch=if($env:PROCESSOR_ARCHITEW6432){$env:PROCESSOR_ARCHITEW6432}else{$env:PROCESSOR_ARCHITECTURE}
 $arch=switch($osArch){'AMD64' {'AMD64'} 'ARM64' {'ARM64'} default {throw "Unsupported OS architecture: $osArch"}}
 if (-not [Environment]::Is64BitProcess) {throw 'Use a 64-bit PowerShell process to avoid registry redirection.'}
 if ([Environment]::OSVersion.Version.Build -lt 26100) {throw 'The touchpad settings API requires Windows 11 24H2 or later.'}
 $qlZip=Join-Path $PackageRoot 'QuickLook_Portable_Clean.zip'
 if(-not(Test-Path -LiteralPath $qlZip)){
  $parts=@(Get-ChildItem -LiteralPath $PackageRoot -Filter 'QuickLook_Portable_Clean.zip.part*' | Sort-Object Name)
  if($parts.Count -ge 2){
   Write-Host '[0/7] Reassembling QuickLook_Portable_Clean.zip from split parts...'
   $outStream=[System.IO.File]::Create($qlZip)
   try {
    foreach($p in $parts){
     $inStream=[System.IO.File]::OpenRead($p.FullName)
     try { $inStream.CopyTo($outStream) } finally { $inStream.Dispose() }
    }
   } finally { $outStream.Dispose() }
  }
 }
 $runtimeFiles=@('AmtPtpControlPanel.exe','MagicForceClick.cs','MagicForceClick.exe','MagicTrackpad_AutoFix.ps1','MagicTrackpad_AutoFix_Silent.vbs','Set_MagicTrackpad_UserSettings.ps1','Register_MagicTrackpad_AutoFix.ps1')
 foreach($file in ($runtimeFiles + @('QuickLook_Portable_Clean.zip',"$arch\AmtPtpDevice.inf","$arch\amtptpdevice.cat"))) {
  if(-not(Test-Path -LiteralPath (Join-Path $PackageRoot $file))) {throw "Missing package file: $file"}
 }
 $signature=Get-AuthenticodeSignature -FilePath (Join-Path $PackageRoot "$arch\amtptpdevice.cat")
 if($signature.Status -ne 'Valid'){throw "Driver catalog signature invalid: $($signature.Status)"}
 $manifestFile=Join-Path $PackageRoot 'SHA256SUMS.json'
 if(Test-Path -LiteralPath $manifestFile){
  foreach($item in (Get-Content -LiteralPath $manifestFile -Raw -Encoding UTF8 | ConvertFrom-Json)){
   $full=Join-Path $PackageRoot $item.Path
   if(-not(Test-Path -LiteralPath $full) -or (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash -ne $item.SHA256){throw "Package checksum mismatch: $($item.Path)"}
  }
 } else {throw 'Package checksum manifest missing.'}
 # Back up values/files before changes; a whole-key .reg backup is for inspection/manual restoration.
 foreach($entry in @(
  @('HKCU\Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad','Touchpad.reg'),
  @('HKCU\Software\Microsoft\Windows\CurrentVersion\Run','Run.reg'),
  @('HKLM\SYSTEM\CurrentControlSet\Services\AmtPtpHidFilter\Parameters','BluetoothParameters.reg'),
  @('HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\WUDF\Services\AmtPtpDeviceUsbUm\Parameters','UsbParameters.reg')
 )) {
  $check=$entry[0].Replace('HKCU\','HKCU:\').Replace('HKLM\','HKLM:\')
  if(Test-Path -LiteralPath $check){
   & reg.exe export $entry[0] (Join-Path $backup $entry[1]) /y | Out-Null
   if($LASTEXITCODE -ne 0){throw "Registry backup failed: $($entry[0])"}
  }
 }
 if(Test-Path -LiteralPath $TargetDir){Copy-Item -LiteralPath $TargetDir -Destination (Join-Path $backup 'PreviousRuntime') -Recurse}
 $oldTask=Get-ScheduledTask -TaskName MagicTrackpad_AutoFix -ErrorAction SilentlyContinue
 if($oldTask){Export-ScheduledTask -TaskName MagicTrackpad_AutoFix | Set-Content -LiteralPath (Join-Path $backup 'PreviousTask.xml') -Encoding Unicode}
 $exeFile=Join-Path $TargetDir 'MagicForceClick.exe'
 Write-Host '[1/7] Deploy runtime files (existing files backed up).'
 $thisSession=(Get-Process -Id $PID).SessionId
 foreach($old in @(Get-Process -Name MagicForceClick -ErrorAction SilentlyContinue | Where-Object {$_.SessionId -eq $thisSession})){
  if($old.Path -and (Test-Path -LiteralPath $old.Path)){
   $oldBackup=Join-Path $backup ('PreviousForceClick_'+$old.Id)
   New-Item -ItemType Directory -Path $oldBackup -Force | Out-Null
   Copy-Item -LiteralPath $old.Path -Destination $oldBackup
  }
  $old | Stop-Process
 }
 New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
 foreach($sub in @('AMD64','ARM64')){Copy-Item -LiteralPath (Join-Path $PackageRoot $sub) -Destination $TargetDir -Recurse -Force}
 foreach($file in $runtimeFiles){Copy-Item -LiteralPath (Join-Path $PackageRoot $file) -Destination (Join-Path $TargetDir $file) -Force}
 # Validate optional rebuild in a candidate file before replacing the supplied executable.
 $csc="$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
 if(Test-Path -LiteralPath $csc){
  $candidate=Join-Path $TargetDir 'MagicForceClick.candidate.exe'
  & $csc /nologo /target:winexe /platform:anycpu /optimize+ /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$candidate" (Join-Path $TargetDir 'MagicForceClick.cs')
  if($LASTEXITCODE -ne 0){throw "ForceClick build failed: $LASTEXITCODE"}
  Move-Item -LiteralPath $candidate -Destination $exeFile -Force
 }
 Write-Host '[2/7] Install signed Apple trackpad driver.'
 & pnputil.exe /add-driver (Join-Path $TargetDir "$arch\AmtPtpDevice.inf") /install
 $driverExit=$LASTEXITCODE
 if($driverExit -notin @(0,3010)){throw "pnputil failed: $driverExit"}
 Write-Host '[3/7] Configure both Bluetooth and USB driver branches.'
 $parameters=@{ButtonDisabled=0;FeedbackClick=394775;FeedbackRelease=20;StopPressure=0;StopSize=[uint32]::MaxValue;IgnoreButtonFinger=0;IgnoreNearFingers=1;PalmRejection=1}
 foreach($key in @('HKLM:\SYSTEM\CurrentControlSet\Services\AmtPtpHidFilter\Parameters','HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\WUDF\Services\AmtPtpDeviceUsbUm\Parameters')){
  if(-not(Test-Path -LiteralPath $key)){New-Item -Path $key -Force | Out-Null}
  foreach($v in $parameters.GetEnumerator()){New-ItemProperty -LiteralPath $key -Name $v.Key -Value $v.Value -PropertyType DWord -Force | Out-Null}
  if((Get-ItemProperty -LiteralPath $key).IgnoreButtonFinger -ne 0){throw "Driver registry verification failed: $key"}
 }
 Write-Host '[4/7] Apply and verify touchpad preferences (tap-drag off, scroll/zoom on).'
 $userResult=& (Join-Path $TargetDir 'Set_MagicTrackpad_UserSettings.ps1') -CursorSpeed $CursorSpeed
 $userResult | Format-List | Out-Host
 Write-Host '[5/7] Register logon, 15-minute and wake-event repair triggers.'
 & (Join-Path $TargetDir 'Register_MagicTrackpad_AutoFix.ps1') -RuntimeDir $TargetDir | Format-List | Out-Host
 & "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File (Join-Path $TargetDir 'MagicTrackpad_AutoFix.ps1')
 if($LASTEXITCODE -ne 0){throw "AutoFix process failed: $LASTEXITCODE"}
 Write-Host '[6/7] Deploy QuickLook and set current-user startup.'
 $qlDir=Join-Path $env:LOCALAPPDATA 'Programs\QuickLook'
 $qlExe=Join-Path $qlDir 'QuickLook.exe'
 $qlCandidate=Join-Path $backup 'QuickLookNew'
 Expand-Archive -LiteralPath (Join-Path $PackageRoot 'QuickLook_Portable_Clean.zip') -DestinationPath $qlCandidate
 if(-not(Test-Path -LiteralPath (Join-Path $qlCandidate 'QuickLook.exe'))){throw 'QuickLook archive root invalid.'}
 $plugins=@(Get-ChildItem -LiteralPath (Join-Path $qlCandidate 'QuickLook.Plugin') -Filter 'QuickLook.Plugin.*.dll' -Recurse -File)
 if($plugins.Count -ne 21){throw "QuickLook plugin count invalid: $($plugins.Count)"}
 Get-Process -Name QuickLook -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $qlExe} | Stop-Process
 if(Test-Path -LiteralPath $qlDir){Copy-Item -LiteralPath $qlDir -Destination (Join-Path $backup 'QuickLookPrevious') -Recurse}
 New-Item -ItemType Directory -Path $qlDir -Force | Out-Null
 Get-ChildItem -LiteralPath $qlCandidate | Copy-Item -Destination $qlDir -Recurse -Force
 $runKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
 if(-not(Test-Path -LiteralPath $runKey)){New-Item -Path $runKey -Force | Out-Null}
 $qlCommand='"'+$qlExe+'" /autorun'
 New-ItemProperty -LiteralPath $runKey -Name QuickLook -Value $qlCommand -PropertyType String -Force | Out-Null
 Write-Host '[7/7] Set ForceClick startup and launch per-user tools.'
 $forceCommand='"'+$exeFile+'" '+$ForceClickThreshold
 New-ItemProperty -LiteralPath $runKey -Name MagicForceClick -Value $forceCommand -PropertyType String -Force | Out-Null
 # Use a Limited interactive task so preview tools run at normal user integrity after elevated installation.
 $user=[Security.Principal.WindowsIdentity]::GetCurrent().Name
 $launchTask='MagicTrackpad_LaunchOnce_'+[guid]::NewGuid().ToString('N')
 try {
  $qlLiteral=$qlExe.Replace("'","''")
  $fcLiteral=$exeFile.Replace("'","''")
  $launchCode="Start-Process -FilePath '$qlLiteral' -ArgumentList '/autorun' -WindowStyle Hidden; Start-Process -FilePath '$fcLiteral' -ArgumentList '$ForceClickThreshold' -WindowStyle Hidden"
  $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($launchCode))
  $launchAction=New-ScheduledTaskAction -Execute "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -Argument "-NoProfile -WindowStyle Hidden -EncodedCommand $encoded"
  $limited=New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
  Register-ScheduledTask -TaskName $launchTask -Action $launchAction -Principal $limited | Out-Null
  Start-ScheduledTask -TaskName $launchTask
  $deadline=(Get-Date).AddSeconds(15)
  do {
   Start-Sleep -Milliseconds 300
   $ql=Get-Process -Name QuickLook -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $qlExe}
   $fc=Get-Process -Name MagicForceClick -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $exeFile}
  } until (($ql -and $fc) -or (Get-Date) -gt $deadline)
  if(-not($ql -and $fc)){throw 'Preview process startup verification failed.'}
 } finally {Unregister-ScheduledTask -TaskName $launchTask -Confirm:$false -ErrorAction SilentlyContinue}
 if((Get-ItemProperty -LiteralPath $runKey).QuickLook -ne $qlCommand -or (Get-ItemProperty -LiteralPath $runKey).MagicForceClick -ne $forceCommand){throw 'Startup registry verification failed.'}
 if($EnableThreeFingerDrag){
  if($arch -ne 'AMD64'){throw 'The bundled optional three-finger app is x64 only.'}
  Add-AppxPackage -Path (Join-Path $PackageRoot 'ThreeFingersDragOnWindows_2.0.7_x64.Msix')
  Write-Warning 'Optional app installed; enable/configure its dragging mode in its own UI.'
 }
 $report=[pscustomobject]@{Time=(Get-Date -Format o);Backup=$backup;Runtime=$TargetDir;DriverInstallExit=$driverExit;RebootRequested=($driverExit -eq 3010);Touchpad=$userResult;QuickLookProcessId=@($ql.Id);ForceClickProcessId=@($fc.Id);PhysicalGestures='Pending user test';BluetoothPairing='Manual, if needed'}
 $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $backup 'Install.result.json') -Encoding UTF8
 Write-Host "Deployment checks passed. Backup/report: $backup"
 Write-Host 'Test pointer, taps, scroll, press-drag and deep-press preview. Separate second haptic feedback is unsupported.'
 if($driverExit -eq 3010){Write-Warning 'The driver installer requested a reboot.'}
} finally {Stop-Transcript | Out-Null}
