@echo off
cd /d "%~dp0"
dotnet publish src\JTCStamper.App\JTCStamper.App.csproj -c Release -p:PublishProfile=Portable -o "%~dp0dist\win-x64"
if errorlevel 1 exit /b 1
echo Portable executable: dist\win-x64\JTCStamper.App.exe
