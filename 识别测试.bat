@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo 识别测试器会在 3 秒后开始截图，并连续识别 8 秒。
echo 请现在切回游戏画面，等待识别结束。
echo.
where python >nul 2>nul
if %errorlevel%==0 (
    python "%~dp0recognition_probe.py" --delay 3 --watch 8 --interval 0.25
) else (
    py -3 "%~dp0recognition_probe.py" --delay 3 --watch 8 --interval 0.25
)
echo.
pause
