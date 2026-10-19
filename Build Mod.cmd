@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Build.ps1" %*
pause
