@echo off
setlocal

set INSTALL_DIR=C:\Users\grant.rogers\My Apps\VoiceDictation
set DIST_EXE=.\dist\VoiceDictation.exe
set INSTALL_EXE=%INSTALL_DIR%\VoiceDictation.exe
set BACKUP_EXE=%INSTALL_DIR%\VoiceDictation.exe.bak

echo VoiceDictation Updater
echo ======================
echo.

if not exist "%DIST_EXE%" (
    echo ERROR: No build found at %DIST_EXE%
    echo Run build.bat first.
    echo.
    pause
    exit /b 1
)

echo Stopping running instance...
taskkill /IM VoiceDictation.exe /F >nul 2>&1
timeout /t 1 /nobreak >nul

echo Backing up old version...
if exist "%INSTALL_EXE%" copy /Y "%INSTALL_EXE%" "%BACKUP_EXE%" >nul

echo Installing new version...
copy /Y "%DIST_EXE%" "%INSTALL_EXE%" >nul
if errorlevel 1 (
    echo ERROR: Failed to copy new exe. Is the install folder correct?
    echo Expected: %INSTALL_DIR%
    pause
    exit /b 1
)

echo Restarting VoiceDictation...
start "" "%INSTALL_EXE%"

echo.
echo Update installed successfully!
echo.
set /p PUSH="Push source changes to GitHub? (Y/N): "
if /i "%PUSH%"=="Y" goto :push
echo Skipping GitHub push.
goto :done

:push
echo.
echo Pushing to GitHub...
git add -A
git commit -m "Update VoiceDictation source"
git push
if errorlevel 1 (
    echo WARNING: Git push failed. Make sure git is configured and you have network access.
) else (
    echo Pushed to GitHub successfully!
)

:done
echo.
pause
