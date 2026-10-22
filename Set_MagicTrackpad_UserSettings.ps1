# Windows PowerShell 5.1 / PowerShell 7, current interactive user only.
[CmdletBinding()]
param(
    [ValidateRange(1,20)][int]$CursorSpeed = 14,
    [switch]$ReadOnly
)
$ErrorActionPreference = 'Stop'
if (-not ('MagicTrackpad.UserSettings' -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
namespace MagicTrackpad {
 public static class UserSettings {
  [StructLayout(LayoutKind.Sequential)]
  public struct Parameters {
   public uint versionNumber, maxSupportedContacts, legacyTouchpadFeatures, systemFlags,
    userFlags, sensitivityLevel, cursorSpeed, feedbackIntensity, clickForceSensitivity,
    rightClickZoneWidth, rightClickZoneHeight;
  }
  [DllImport("user32.dll", EntryPoint="SystemParametersInfoW", SetLastError=true)]
  private static extern bool Spi(uint action, uint size, ref Parameters p, uint flags);
  public static Parameters Read() {
   var p = new Parameters { versionNumber = 1 };
   if (!Spi(0xAE, (uint)Marshal.SizeOf(typeof(Parameters)), ref p, 0))
    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
   return p;
  }
  public static Parameters Apply(uint speed) {
   var p = Read();
   // Preserve unrelated options. 0x08 = tapAndDragEnabled; 0x80 = panEnabled.
   p.userFlags = (p.userFlags | 0x01u | 0x04u | 0x10u | 0x80u | 0x100u) & ~0x08u & ~0x200u;
   p.sensitivityLevel = 0;
   p.cursorSpeed = speed;
   if (!Spi(0xAF, (uint)Marshal.SizeOf(typeof(Parameters)), ref p, 3))
    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
   var after = Read();
   const uint mask = 0x01u | 0x04u | 0x08u | 0x10u | 0x80u | 0x100u | 0x200u;
   if ((after.userFlags & mask) != (p.userFlags & mask) || after.cursorSpeed != speed || after.sensitivityLevel != 0)
    throw new InvalidOperationException("Touchpad API verification failed.");
   return after;
  }
 }
}
"@
}
$before = [MagicTrackpad.UserSettings]::Read()
if ($ReadOnly) { $after = $before } else { $after = [MagicTrackpad.UserSettings]::Apply([uint32]$CursorSpeed) }
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad'
# Keep the registry preferences consistent with the successfully verified API result.
if (-not $ReadOnly) {
    if (-not (Test-Path -LiteralPath $key)) { New-Item -Path $key -Force | Out-Null }
    $values = @{CursorSpeed=$CursorSpeed; TapAndDrag=0; PanEnabled=1; ZoomEnabled=1;
                TapsEnabled=1; TwoFingerTapEnabled=1; LeaveOnWithMouse=1; AAPThreshold=0; ScrollDirection=0}
    foreach ($entry in $values.GetEnumerator()) {
        New-ItemProperty -LiteralPath $key -Name $entry.Key -Value $entry.Value -PropertyType DWord -Force | Out-Null
    }
}
$reg = Get-ItemProperty -LiteralPath $key
[pscustomobject]@{
    User=[Security.Principal.WindowsIdentity]::GetCurrent().Name
    Applied=(-not $ReadOnly); BeforeFlags=$before.userFlags; BeforeSpeed=$before.cursorSpeed
    ApiFlags=$after.userFlags; ApiCursorSpeed=$after.cursorSpeed
    ApiTapAndDrag=[bool]($after.userFlags -band 0x08); ApiPan=[bool]($after.userFlags -band 0x80)
    ApiZoom=[bool]($after.userFlags -band 0x100)
    RegistryCursorSpeed=$reg.CursorSpeed; RegistryTapAndDrag=$reg.TapAndDrag
    RegistryPan=$reg.PanEnabled; RegistryZoom=$reg.ZoomEnabled
}
