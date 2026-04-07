@echo off
setlocal

echo Building VoiceDictation...
echo.

dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableWindowsTargeting=true -o .\dist

if errorlevel 1 (
    echo.
    echo ERROR: Build failed.
    echo Make sure the .NET 8 SDK is installed: https://dotnet.microsoft.com/en-us/download/dotnet/8.0
    echo.
    pause
    exit /b 1
)

echo.
echo Build succeeded! Launching new version for testing...
echo Close it and run update.bat when you're happy.
echo.
start "" ".\dist\VoiceDictation.exe"
pause
