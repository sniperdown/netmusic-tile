@echo off
rem ============================================================
rem  netmusic-tile - build
rem
rem  Builds the WPF app and stages the result into
rem  release\netmusic-tile, which install.cmd copies from.
rem
rem  If this checkout carries an offline NuGet cache (.nuget\),
rem  it is used automatically - that is how the project was
rem  developed on a machine where nuget.org was awkward to reach.
rem
rem  ASCII only on purpose: a .cmd is read in the OEM code page
rem  and non-ASCII text turns into mojibake in the console.
rem ============================================================

setlocal
set ROOT=%~dp0
set PROJ=%ROOT%app\NetMusicTile.csproj
set BIN=%ROOT%app\bin\Release\net6.0-windows10.0.19041.0
set OUT=%ROOT%release\netmusic-tile

where dotnet >nul 2>&1
if errorlevel 1 (
  echo   ERROR: dotnet was not found on PATH.
  echo   Install the .NET 6 SDK: https://dotnet.microsoft.com/download
  echo.
  pause
  exit /b 1
)

if exist "%ROOT%.nuget\NuGet.offline.config" (
  echo   Using the offline NuGet cache in .nuget
  set APPDATA=%ROOT%.nuget
  set NUGET_PACKAGES=%ROOT%.nuget\global-packages
  set DOTNET_CLI_HOME=%ROOT%.nuget
  dotnet build "%PROJ%" -c Release --configfile "%ROOT%.nuget\NuGet.offline.config"
) else (
  dotnet build "%PROJ%" -c Release
)

if errorlevel 1 (
  echo.
  echo   BUILD FAILED - see the errors above.
  echo.
  pause
  exit /b 1
)

if not exist "%OUT%" mkdir "%OUT%" >nul 2>&1
del /q "%OUT%\*" >nul 2>&1
xcopy "%BIN%" "%OUT%\" /Y /I >nul
del /q "%OUT%\*.pdb" >nul 2>&1
del /q "%OUT%\netmusic-tile.log" >nul 2>&1
del /q "%OUT%\signal.txt" >nul 2>&1

echo.
echo   Staged into %OUT%
exit /b 0
