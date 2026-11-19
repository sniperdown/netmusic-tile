@echo off
rem Undo what install.cmd added: the autostart shortcut and the running program.
rem Does NOT delete C:\netmusic-tile - remove that folder by hand if you want it gone.
setlocal

echo.
echo   Removing the autostart shortcut...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$p = [Environment]::GetFolderPath('Startup') + '\netmusic-tile.lnk'; if (Test-Path $p) { Remove-Item $p -Force; Write-Host ('  removed ' + $p) } else { Write-Host '  no autostart shortcut found' }"

echo   Stopping the running program...
taskkill /IM netmusic-tile.exe /F >nul 2>&1
if errorlevel 1 (
  echo   it was not running
) else (
  echo   stopped
)

echo.
echo   Done. The program will no longer start with Windows.
echo   Files are still in C:\netmusic-tile - delete that folder to uninstall fully.
echo.
pause
