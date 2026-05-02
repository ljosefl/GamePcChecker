@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

set OUT=publish\GamePcChecker-win-x64
echo Сборка self-contained (всё в одной папке, .NET на ПК не нужен)...
echo Папка вывода: %OUT%
echo.

dotnet publish "src\GamePcChecker.App\GamePcChecker.App.csproj" ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:DebugType=none ^
  -o "%OUT%"

if errorlevel 1 (
  echo.
  echo Ошибка сборки.
  pause
  exit /b 1
)

echo.
echo Готово. Запуск: %OUT%\GamePcChecker.exe
echo Можно архивировать всю папку %OUT% и переносить на другой ПК.
echo.
pause
