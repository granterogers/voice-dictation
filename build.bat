@echo off
setlocal

echo Building VoiceDictation...
echo.

dotnet publish -c Release -o .\dist

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
echo Don't forget to copy groq_key.txt into .\dist before running.
echo.
start explorer .\dist
pause
