@echo off
REM Build script for LARGERslicer Yak packages (Rhino 8 + Rhino 7, Windows)
REM Usage: build-package.bat [version]
REM
REM Builds all target frameworks and creates two clean packages in dist\:
REM   largerslicer-<version>-rh8_0-any.yak  (net48 + net7.0 + net7.0-windows)
REM   largerslicer-<version>-rh7_0-any.yak  (net48 only, from manifest-rh7.yml)
REM Each package is built from its own staging folder, so only the manifest,
REM icon, README, LICENSE and plugin binaries end up in the package.

setlocal enabledelayedexpansion
cd /d "%~dp0"

echo LARGERslicer Package Builder
echo ================================

REM Check if version argument provided
if not "%~1"=="" (
    set "VERSION=%~1"
    echo Using provided version: !VERSION!

    REM Update version in .csproj, Yak manifests and yak.yml
    powershell -NoProfile -Command "$v='!VERSION!'; $f=(Resolve-Path 'LARGERslicer.csproj').Path; $c=[IO.File]::ReadAllText($f); foreach ($t in 'Version','InformationalVersion') { $c=$c -replace ('<'+$t+'>.*</'+$t+'>'),('<'+$t+'>'+$v+'</'+$t+'>') }; foreach ($t in 'AssemblyVersion','FileVersion') { $c=$c -replace ('<'+$t+'>.*</'+$t+'>'),('<'+$t+'>'+$v+'.0</'+$t+'>') }; [IO.File]::WriteAllText($f,$c)"
    powershell -NoProfile -Command "$v='!VERSION!'; foreach ($n in 'manifest.yml','manifest-rh7.yml','yak.yml') { $f=(Resolve-Path $n).Path; $c=[IO.File]::ReadAllText($f); $c=$c -replace '(?m)^version:.*$',('version: '+$v); [IO.File]::WriteAllText($f,$c) }"

    echo Updated version to !VERSION!
) else (
    REM Read current version from .csproj
    for /f "tokens=2 delims=<>" %%a in ('findstr /C:"<Version>" LARGERslicer.csproj') do set "VERSION=%%a"
    echo Using current version: !VERSION!
)

REM Manifests must match the assembly version, otherwise the package is mislabeled
for %%m in (manifest.yml manifest-rh7.yml) do (
    set "MV="
    for /f "tokens=2" %%v in ('findstr /B /C:"version:" %%m') do set "MV=%%v"
    if not "!MV!"=="!VERSION!" (
        echo Error: %%m has version !MV!, LARGERslicer.csproj has !VERSION!
        echo Run build-package.bat !VERSION! to sync all version numbers
        exit /b 1
    )
)

REM Find yak: PATH first, then the one shipped with Rhino 8
set "YAK="
for /f "delims=" %%y in ('where yak 2^>nul') do if not defined YAK set "YAK=%%y"
if not defined YAK if exist "C:\Program Files\Rhino 8\System\Yak.exe" set "YAK=C:\Program Files\Rhino 8\System\Yak.exe"
if not defined YAK (
    echo Error: yak command not found
    echo Install Rhino 8 or the Yak CLI: https://www.rhino3d.com/download/yak
    exit /b 1
)

REM Clean previous builds
echo.
echo Cleaning previous builds...
dotnet clean -c Release
if exist bin\Release rmdir /S /Q bin\Release

REM Build the project
echo.
echo Building project for all target frameworks...
dotnet build -c Release
if errorlevel 1 (
    echo Build failed.
    exit /b 1
)

echo Build successful

echo.
echo Syncing packaged runtime folders...
if exist net48 rmdir /S /Q net48
if exist net7.0 rmdir /S /Q net7.0
if exist net7.0-windows rmdir /S /Q net7.0-windows
mkdir net48
mkdir net7.0
mkdir net7.0-windows
if not exist dist mkdir dist
xcopy /E /I /Y bin\Release\net48\* net48\ >nul
xcopy /E /I /Y bin\Release\net7.0\* net7.0\ >nul
xcopy /E /I /Y bin\Release\net7.0-windows\* net7.0-windows\ >nul
copy /Y bin\Release\net48\LARGERslicer.gha LARGERslicer.gha >nul
copy /Y bin\Release\net48\LARGERslicer.gha dist\LARGERslicer.gha >nul
copy /Y bin\Release\net7.0\LARGERslicer.gha dist\LARGERslicer-mac.gha >nul
copy /Y bin\Release\net7.0-windows\LARGERslicer.gha dist\LARGERslicer-win.gha >nul

REM Keep manual-install bundle in sync with the package build.
if not exist "Plugin Installation Files\LARGERSlicer" mkdir "Plugin Installation Files\LARGERSlicer"
copy /Y bin\Release\net48\LARGERslicer.gha "Plugin Installation Files\LARGERSlicer\LARGERslicer.gha" >nul
if exist bin\Release\net48\LARGERslicer.pdb copy /Y bin\Release\net48\LARGERslicer.pdb "Plugin Installation Files\LARGERSlicer\LARGERslicer.pdb" >nul
if exist bin\Release\net48\Newtonsoft.Json.dll copy /Y bin\Release\net48\Newtonsoft.Json.dll "Plugin Installation Files\LARGERSlicer\Newtonsoft.Json.dll" >nul
echo Packaged binaries synced

REM Stage each package in its own folder (obj\ is ignored by git)
echo.
echo Staging Yak packages...
set "STAGE=obj\yak-staging"
if exist "%STAGE%" rmdir /S /Q "%STAGE%"
mkdir "%STAGE%\rh8"
mkdir "%STAGE%\rh7"

REM Rhino 8: one subfolder per target framework, Rhino picks the matching one
for %%t in (net48 net7.0 net7.0-windows) do xcopy /E /I /Y bin\Release\%%t\* "%STAGE%\rh8\%%t\" >nul
copy /Y manifest.yml "%STAGE%\rh8\manifest.yml" >nul

REM Rhino 7: net48 binaries at the package root
xcopy /E /I /Y bin\Release\net48\* "%STAGE%\rh7\" >nul
copy /Y manifest-rh7.yml "%STAGE%\rh7\manifest.yml" >nul

for %%p in (rh8 rh7) do (
    copy /Y icon.png "%STAGE%\%%p\" >nul
    copy /Y README.md "%STAGE%\%%p\" >nul
    if exist ..\LICENSE copy /Y ..\LICENSE "%STAGE%\%%p\" >nul
    del /S /Q "%STAGE%\%%p\*.pdb" >nul 2>&1
)

REM Create Yak packages
echo.
echo Creating Yak packages...
for %%p in (rh8 rh7) do (
    pushd "%STAGE%\%%p"
    "%YAK%" build --platform any
    if errorlevel 1 (
        popd
        echo Package creation failed ^(%%p^).
        exit /b 1
    )
    popd
    set "FOUND="
    for %%f in ("%STAGE%\%%p\*.yak") do (
        move /Y "%%f" dist\ >nul
        set "FOUND=1"
        set "YAK_%%p=dist\%%~nxf"
    )
    if not defined FOUND (
        echo Error: No .yak file created ^(%%p^)
        exit /b 1
    )
)

echo.
echo Packages created:
echo   !YAK_rh8!
echo   !YAK_rh7!
echo.
echo Next steps:
echo   1. Test locally: drag the .yak file for your Rhino version onto the Rhino window
echo   2. Publish both:
echo      "%YAK%" push !YAK_rh8!
echo      "%YAK%" push !YAK_rh7!
echo.

endlocal
