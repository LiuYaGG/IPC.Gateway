@echo off
chcp 65001 >nul
cd /d "%~dp0"
set "GO_EXE=C:\Program Files\Go\bin\go.exe"
if not exist "%GO_EXE%" (
  echo 未找到 Go: %GO_EXE%
  pause
  exit /b 1
)
if not exist dist mkdir dist
"%GO_EXE%" test ./...
if errorlevel 1 pause & exit /b 1
"%GO_EXE%" build -trimpath -ldflags="-s -w" -o dist\BeckhoffAdsSimulator.exe .
if errorlevel 1 pause & exit /b 1
copy /y points.json dist\points.json >nul
echo 编译完成: dist\BeckhoffAdsSimulator.exe
pause
