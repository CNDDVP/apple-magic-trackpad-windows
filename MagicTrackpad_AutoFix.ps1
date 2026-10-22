#Requires -RunAsAdministrator
# Narrow repair: current Apple trackpad nodes only; preserve unrelated devices/settings.
$ErrorActionPreference='Stop'
$events=New-Object System.Collections.Generic.List[object]
function Record([string]$step,[string]$status,[string]$detail) {
    $events.Add([pscustomobject]@{Step=$step;Status=$status;Detail=$detail})
}
try {
    $nodes=@(Get-PnpDevice -PresentOnly | Where-Object {
        $_.InstanceId -match 'VID_05AC&PID_(0324|0265)|VID&0001004C_PID&(0324|0265)'
    })
    foreach ($dev in $nodes) {
        if ($dev.InstanceId -match 'VID_05AC&PID_0324&MI_00') {
            $problem=(Get-PnpDeviceProperty -InstanceId $dev.InstanceId -KeyName DEVPKEY_Device_ProblemCode -ErrorAction SilentlyContinue).Data
            if ($problem -eq 10) {
                try { Disable-PnpDevice -InstanceId $dev.InstanceId -Confirm:$false; Record 'MI_00' 'DisabledCode10' $dev.InstanceId }
                catch { Record 'MI_00' 'Error' $_.Exception.Message }
            }
        }
        $key="HKLM:\SYSTEM\CurrentControlSet\Enum\$($dev.InstanceId)\Device Parameters"
        if (Test-Path -LiteralPath $key) {
            try {
                foreach($name in @('EnhancedPowerManagementEnabled','AllowIdleIrpInD3','SelectiveSuspendEnabled')) {
                    New-ItemProperty -LiteralPath $key -Name $name -Value 0 -PropertyType DWord -Force | Out-Null
                }
            } catch { Record 'PowerSettings' 'Error' $_.Exception.Message }
        }
    }
    Record 'PowerSettings' 'Checked' ([string]$nodes.Count+' present Apple nodes')
} catch { Record 'DeviceEnumeration' 'Error' $_.Exception.Message }
foreach($key in @(
 'HKLM:\SYSTEM\CurrentControlSet\Services\AmtPtpHidFilter\Parameters',
 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\WUDF\Services\AmtPtpDeviceUsbUm\Parameters'
)) {
    if (Test-Path -LiteralPath $key) {
        try {
            New-ItemProperty -LiteralPath $key -Name IgnoreButtonFinger -Value 0 -PropertyType DWord -Force | Out-Null
            if ((Get-ItemProperty -LiteralPath $key).IgnoreButtonFinger -ne 0) { throw 'Registry verification failed.' }
            Record 'IgnoreButtonFinger' 'Verified0' $key
        } catch { Record 'IgnoreButtonFinger' 'Error' $_.Exception.Message }
    } else { Record 'IgnoreButtonFinger' 'BranchAbsent' $key }
}
# Bounded asynchronous read: an unresponsive USB pipe must not stall the periodic task.
$pipe=$null
try {
    $pipe=[IO.Pipes.NamedPipeClientStream]::new('.', 'AmtPtpControlPanelUsbUmInterface', [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
    $pipe.Connect(300)
    $pipe.WriteByte(0); $pipe.Flush()
    $buffer=New-Object byte[] 2
    $pending=$pipe.BeginRead($buffer,0,2,$null,$null)
    if (-not $pending.AsyncWaitHandle.WaitOne(1000)) { throw 'USB reload reply timeout.' }
    $bytes=$pipe.EndRead($pending)
    if ($bytes -eq 0) { throw 'USB pipe closed without reply.' }
    Record 'UsbReload' 'ReplyReceived' ([string]$bytes+' bytes')
} catch { Record 'UsbReload' 'UnavailableOrFailed' $_.Exception.Message }
finally { if ($pipe) {$pipe.Dispose()} }
try {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
public static class MagicTrackpadBtReload {
 [DllImport("kernel32.dll", EntryPoint="CreateFileW", CharSet=CharSet.Unicode, SetLastError=true)]
 static extern SafeFileHandle Open(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
 [DllImport("kernel32.dll", SetLastError=true)]
 static extern bool DeviceIoControl(SafeFileHandle h,uint code,IntPtr input,uint inputSize,IntPtr output,uint outputSize,out uint bytes,IntPtr overlapped);
 public static bool Reload() {
  using(var h=Open(@"\\.\AmtPtpControlDeviceUm",0xC0000000u,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
   if(h.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
   uint bytes;
   if(!DeviceIoControl(h,0x00222000u,IntPtr.Zero,0,IntPtr.Zero,0,out bytes,IntPtr.Zero))
    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
   return true;
  }
 }
}
"@
    [void][MagicTrackpadBtReload]::Reload()
    Record 'BluetoothReload' 'Success' 'IOCTL 0x00222000'
} catch { Record 'BluetoothReload' 'UnavailableOrFailed' $_.Exception.Message }
$result=[pscustomobject]@{Time=(Get-Date -Format o);Events=@($events.ToArray())}
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'MagicTrackpad_AutoFix.last.json') -Encoding UTF8
$result
