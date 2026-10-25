# Shared by the full installer, updater and package verification tests.
function Get-MagicTrackpadSuiteFiles {
 @('MagicForceClick.exe','MagicForceClick.cs','MagicForceClick.Core.cs','MagicForceClick.Settings.cs','MagicForceClick.Preview.cs','MagicForceClick.json','Open_MagicTrackpad_Settings.cmd','Update_MagicForceClick.ps1','Restore_MagicForceClick.ps1','Suite_Integration.ps1','RELEASE_VERSION','Open_MagicTrackpad_ControlCenter.cmd','MagicControlCenter\MagicControlCenter.exe','MagicControlCenter\MagicControlCenter.exe.config','MagicControlCenter\MagicControlCenter.ico','MagicControlCenter\MAGIC_CONTROL_CENTER.md')
}
function Close-MagicTrackpadControlCenter([string]$RuntimeDir) {
 $target=Join-Path $RuntimeDir 'MagicControlCenter\MagicControlCenter.exe'
 foreach($process in @(Get-Process -Name MagicControlCenter -ErrorAction SilentlyContinue|Where-Object{$_.Path -eq $target})) {
  if(-not $process.CloseMainWindow() -or -not $process.WaitForExit(5000)){throw 'Close the control center after saving or discarding edits, then retry.'}
 }
}
function Get-MagicTrackpadShortcutPath {
 Join-Path ([Environment]::GetFolderPath('Desktop')) '妙控板控制中心.lnk'
}
function New-MagicTrackpadShortcut([string]$RuntimeDir) {
 $path=Get-MagicTrackpadShortcutPath
 $shell=New-Object -ComObject WScript.Shell
 $link=$shell.CreateShortcut($path)
 $link.TargetPath=Join-Path $RuntimeDir 'MagicControlCenter\MagicControlCenter.exe'
 $link.WorkingDirectory=Split-Path $link.TargetPath -Parent
 $link.Description='Magic Trackpad 控制中心'
 $link.IconLocation=(Join-Path $RuntimeDir 'MagicControlCenter\MagicControlCenter.ico')+',0'
 $link.Save()
 if($shell.CreateShortcut($path).TargetPath -ne $link.TargetPath){throw 'Desktop shortcut verification failed.'}
}
