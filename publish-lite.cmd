@echo off
cd /d "%~dp0"
dotnet publish src\JTCStamper.App\JTCStamper.App.csproj -c Release -p:PublishProfile=Lite -o "%~dp0dist\lite-win-x64"
if errorlevel 1 exit /b 1
echo Lite executable: dist\lite-win-x64\JTCStamper.App.exe
