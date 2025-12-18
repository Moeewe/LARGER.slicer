@echo off
REM Build script for LARGERslicer Yak package (Windows)
REM Usage: build-package.bat [version]

setlocal enabledelayedexpansion

echo LARGERslicer Package Builder
echo ================================

REM Check if version argument provided
if not "%~1"=="" (
    set VERSION=%~1
    echo Using provided version: %VERSION%
    
    REM Update version in .csproj (requires PowerShell or sed)
    powershell -Command "(Get-Content LARGERslicer.csproj) -replace '<Version>.*</Version>', '<Version>%VERSION%</Version>' | Set-Content LARGERslicer.csproj"
    
    REM Update version in yak.yml
    powershell -Command "(Get-Content yak.yml) -replace '^version:.*', 'version: %VERSION%' | Set-Content yak.yml"
    
    echo Updated version to %VERSION%
) else (
    REM Read current version from .csproj
    for /f "tokens=2 delims=<>" %%a in ('findstr /C:"<Version>" LARGERslicer.csproj') do set VERSION=%%a
    echo Using current version: %VERSION%
)

REM Check if yak is installed
where yak >nul 2>&1
if %errorlevel% neq 0 (
    echo Error: yak command not found
    echo Please install Yak CLI from: https://www.rhino3d.com/download/yak
    exit /b 1
)

REM Check if logged in
yak whoami >nul 2>&1
if %errorlevel% neq 0 (
    echo Warning: Not logged in to Yak
    echo Run 'yak login' to authenticate
)

REM Clean previous builds
echo.
echo Cleaning previous builds...
dotnet clean -c Release
del /Q *.yak 2>nul

REM Build the project
echo.
echo Building project for all target frameworks...
dotnet build -c Release

if %errorlevel% neq 0 (
    echo Build failed!
    exit /b 1
)

echo Build successful

REM Create Yak package
echo.
echo Creating Yak package...
yak build

if %errorlevel% neq 0 (
    echo Package creation failed!
    exit /b 1
)

REM Find the created .yak file
for %%f in (*.yak) do set YAK_FILE=%%f

if "!YAK_FILE!"=="" (
    echo Error: No .yak file created
    exit /b 1
)

echo.
echo Package created: !YAK_FILE!
echo.
echo Next steps:
echo   1. Test locally: yak install !YAK_FILE! --source .
echo   2. Publish: yak push !YAK_FILE!
echo.

endlocal














