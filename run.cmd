@echo off
cd /d "%~dp0"
dotnet run --project src\JTCStamper.App\JTCStamper.App.csproj
if errorlevel 1 pause
