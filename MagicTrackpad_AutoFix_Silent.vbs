Set ws = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
ps1Path = fso.BuildPath(scriptDir, "MagicTrackpad_AutoFix.ps1")
psExe = ws.ExpandEnvironmentStrings("%SystemRoot%") & "\System32\WindowsPowerShell\v1.0\powershell.exe"
cmd = Chr(34) & psExe & Chr(34) & " -NoProfile -ExecutionPolicy Bypass -File " & Chr(34) & ps1Path & Chr(34)
result = ws.Run(cmd, 0, True)
WScript.Quit result
