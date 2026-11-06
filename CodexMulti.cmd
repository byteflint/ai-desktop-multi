@echo off
setlocal
set "EXE=%~dp0AiDesktopMulti.exe"
if "%~1"=="" (
    if exist "%EXE%" (
        start "" "%EXE%"
        goto :eof
    )
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0CodexMulti.ps1" %*
endlocal
