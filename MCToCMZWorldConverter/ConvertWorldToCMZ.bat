@echo off
setlocal

set "EXE=%~dp0MCToCMZWorldConverter.exe"
set "CONFIG=%~dp0config.json"

"%EXE%" "%CONFIG%"

echo.
pause
