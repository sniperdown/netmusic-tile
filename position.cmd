@echo off
rem ============================================================
rem  netmusic-tile - position / size picker
rem
rem  Opens a draggable frame the size of the panel:
rem     drag it            = move
rem     drag the corner    = resize (kept square)
rem     arrow keys         = nudge 1 px (Shift = 10 px)
rem     Enter / Save       = write config.json
rem     Esc / Cancel       = leave everything as it was
rem
rem  The running panel re-reads config.json by itself (it polls
rem  the file timestamp), so it moves immediately - no restart.
rem
rem  usage:
rem     position.cmd                     -> the installed copy
rem     position.cmd D:\start11-music    -> a copy in that folder
rem
rem  ASCII only on purpose: a .cmd is read in the OEM code page
rem  and non-ASCII text turns into mojibake in the console.
rem ============================================================

setlocal
set DST=C:\netmusic-tile
if not "%~1"=="" set DST=%~1
set EXE=%DST%\netmusic-tile.exe

if not exist "%EXE%" (
  echo.
  echo   Not installed at %DST%
  echo   Run install.cmd first, or pass the folder:
  echo     position.cmd D:\start11-music
  echo.
  pause
  exit /b 1
)

start "" /D "%DST%" "%EXE%" --calibrate
exit /b 0
