Set sh = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
dir = Left(WScript.ScriptFullName, InStrRev(WScript.ScriptFullName, "\"))
exe = dir & "AiDesktopMulti.exe"
If fso.FileExists(exe) Then
    sh.Run """" & exe & """", 1, False
Else
    sh.Run "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & dir & "CodexMulti.ps1"" menu", 0, False
End If
