@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\InstallNativeUpdate.ps1" %*
pause
