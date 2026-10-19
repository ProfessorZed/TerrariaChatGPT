@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\InstallMod.ps1" %*
pause
