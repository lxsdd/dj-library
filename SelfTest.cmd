@echo off
setlocal
cd /d "%~dp0"
if not exist "DJLibrary.exe" call BuildOnly.cmd
if not exist "DJLibrary.exe" exit /b 1
"DJLibrary.exe" --self-test
exit /b %errorlevel%
