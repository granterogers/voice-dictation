@echo off
setlocal

echo Building VoiceDictation...
echo.

dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\dist

if errorlevel 1 (
    echo.
    echo ERROR: Build failed.
    echo Make sure the .NET 8 SDK is installed: https://dotnet.microsoft.com/en-us/download/dotnet/8.0
    echo.
    pause
    exit /b 1
)

echo.
echo Build succeeded! Output is in the .\dist folder.
echo Launching new version for testing...
echo.
start "" ".\dist\VoiceDictation.exe"
echo.
echo Test the new version. When ready, run update.bat to install it.
pause
