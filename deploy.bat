@echo off
setlocal

set INSTALL_DIR=C:\Users\grant.rogers\My Apps\VoiceDictation
set REPO_URL=https://github.com/granterogers/voice-dictation.git
set WORK_DIR=%TEMP%\VoiceDictation-build

echo ============================================
echo  VoiceDictation Updater
echo ============================================
echo.

:: ---- Step 1: Pull latest source ----
echo [1/4] Getting latest source from GitHub...
if exist "%WORK_DIR%\.git" (
    cd /d "%WORK_DIR%"
    git pull origin main
) else (
    rmdir /s /q "%WORK_DIR%" 2>nul
    git clone "%REPO_URL%" "%WORK_DIR%"
)
if errorlevel 1 (
    echo ERROR: Could not get source from GitHub.
    echo Make sure git is installed: https://git-scm.com/download/win
    pause & exit /b 1
)

:: ---- Step 2: Build ----
echo.
echo [2/4] Building...
cd /d "%WORK_DIR%"
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableWindowsTargeting=true -o "%WORK_DIR%\dist"
if errorlevel 1 (
    echo.
    echo ERROR: Build failed.
    echo Make sure .NET 8 SDK is installed: https://dotnet.microsoft.com/en-us/download/dotnet/8.0
    pause & exit /b 1
)

:: ---- Step 3: Test ----
echo.
echo [3/4] Launching new version for testing...
echo      (The new version is running from a temp folder - your installed version is unchanged)
echo.
copy /Y "%INSTALL_DIR%\groq_key.txt" "%WORK_DIR%\dist\groq_key.txt" >nul 2>&1
start "" "%WORK_DIR%\dist\VoiceDictation.exe"
echo.
set /p HAPPY="Happy with the new version? Kill it and press Y to install, N to abort: "
if /i not "%HAPPY%"=="Y" (
    echo Aborted. Your installed version is unchanged.
    taskkill /IM VoiceDictation.exe /F >nul 2>&1
    pause & exit /b 0
)

:: ---- Step 4: Install ----
echo.
echo [4/4] Installing...
taskkill /IM VoiceDictation.exe /F >nul 2>&1
timeout /t 1 /nobreak >nul
if exist "%INSTALL_DIR%\VoiceDictation.exe" copy /Y "%INSTALL_DIR%\VoiceDictation.exe" "%INSTALL_DIR%\VoiceDictation.exe.bak" >nul
copy /Y "%WORK_DIR%\dist\VoiceDictation.exe" "%INSTALL_DIR%\VoiceDictation.exe"
if errorlevel 1 (
    echo ERROR: Failed to install. Is the install folder correct?
    echo Expected: %INSTALL_DIR%
    pause & exit /b 1
)
echo Restarting VoiceDictation...
start "" "%INSTALL_DIR%\VoiceDictation.exe"
echo.
echo Done! VoiceDictation has been updated and restarted.
echo.
set /p PUSH="Push source changes to GitHub? (Y/N): "
if /i "%PUSH%"=="Y" (
    cd /d "%WORK_DIR%"
    git push
    echo Pushed to GitHub.
)
echo.
pause
