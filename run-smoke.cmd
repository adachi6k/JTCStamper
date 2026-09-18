@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -File "%~dp0scripts\Invoke-WindowsSmoke.ps1" %*
exit /b %errorlevel%
