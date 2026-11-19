@echo off
rem ============================================================
rem  netmusic-tile - install
rem
rem  WHY A SEPARATE INSTALL STEP
rem  ---------------------------
rem  The tray icon cannot always be registered from every
rem  location: the shell refuses Shell_NotifyIcon with
rem  ERROR_ACCESS_DENIED when the process runs from a folder
rem  the shell restricts (measured: the identical binary gives
rem  err=5 from a restricted working directory and err=0 from a
rem  normal folder). Installing to a normal folder avoids that.
rem
rem  usage:
rem     install.cmd                     -> C:\netmusic-tile
rem     install.cmd D:\start11-music    -> that folder
rem
rem  Builds first if release\netmusic-tile is missing.
rem
rem  ASCII only on purpose: a .cmd is read in the OEM code page
rem  and non-ASCII text turns into mojibake in the console.
rem ============================================================

setlocal
set ROOT=%~dp0
set SRC=%ROOT%release\netmusic-tile
if "%~1"=="" (set DST=C:\netmusic-tile) else (set DST=%~1)

if not exist "%SRC%\netmusic-tile.exe" (
  echo   Nothing staged yet - building first.
  echo.
  call "%ROOT%build.cmd"
  if errorlevel 1 (
    echo   Cannot install without a successful build.
    pause
    exit /b 1
  )
)

echo.
echo   Installing netmusic-tile to %DST%
echo.

rem stop a running copy so the files can be replaced
taskkill /IM netmusic-tile.exe /F >nul 2>&1
timeout /t 1 /nobreak >nul

if not exist "%DST%" mkdir "%DST%" >nul 2>&1
if not exist "%DST%" (
  echo   ERROR: could not create %DST%.
  echo   Try running this file as administrator, or pass a folder you can write to.
  echo.
  pause
  exit /b 1
)

xcopy "%SRC%" "%DST%\" /E /Y /I >nul
if errorlevel 1 (
  echo   ERROR: copy failed.
  echo.
  pause
  exit /b 1
)

echo   Installed to %DST%

rem --- register it to start at logon -------------------------------
rem A shortcut in the Startup folder is the least invasive mechanism:
rem no registry writes, no service, and the user can delete it any time.
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$sh = New-Object -ComObject WScript.Shell; $p = [Environment]::GetFolderPath('Startup') + '\netmusic-tile.lnk'; $s = $sh.CreateShortcut($p); $s.TargetPath = '%DST%\netmusic-tile.exe'; $s.WorkingDirectory = '%DST%'; $s.Description = 'netmusic-tile - music panel for the Start menu'; $s.Save(); Write-Host ('  autostart shortcut -> ' + $p)"
if errorlevel 1 (
  echo   NOTE: could not create the autostart shortcut. Create it by hand:
  echo     Win+R, type  shell:startup , and drop a shortcut to %DST%\netmusic-tile.exe
) else (
  echo   Autostart enabled. Remove it any time with remove-autostart.cmd
)

echo.
start "" /D "%DST%" "%DST%\netmusic-tile.exe"

echo   Done. The program is running now - there is no window.
echo.
echo   HOW TO USE
echo     1. press Win to open the Start menu  -^>  the panel appears
echo     2. right-click the panel             -^>  hides it (program keeps running)
echo     3. run the exe again                 -^>  brings it back
echo     4. Ctrl + right-click the panel      -^>  quit the program
echo     5. tray icon (purple note): double-click = show/hide, right-click = menu
echo.
echo   TO MOVE OR RESIZE THE PANEL
echo     run position.cmd  -  drag the frame, then press Save
echo.
pause
