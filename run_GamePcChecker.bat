@echo off
cd /d "%~dp0"
dotnet run --project "src\GamePcChecker.App\GamePcChecker.App.csproj" -c Release
if errorlevel 1 pause
