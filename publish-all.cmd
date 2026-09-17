@echo off
cd /d "%~dp0"
call publish-portable.cmd
if errorlevel 1 exit /b 1
call publish-lite.cmd
if errorlevel 1 exit /b 1
echo Both distribution builds completed.
