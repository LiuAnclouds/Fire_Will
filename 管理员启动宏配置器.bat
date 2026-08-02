@echo off
chcp 65001 >nul
net session >nul 2>nul
if not "%errorlevel%"=="0" (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
cd /d "%~dp0"
start "" "%~dp0war3_macro_gui.exe"
