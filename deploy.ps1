# VoiceDictation Deploy Script
# Pull latest source, build, test, install, optionally push to GitHub

$InstallDir = "C:\Users\grant.rogers\My Apps\VoiceDictation"
$RepoUrl    = "https://github.com/granterogers/voice-dictation.git"
$WorkDir    = "$env:TEMP\VoiceDictation-build"

function Step($n, $msg) { Write-Host "`n[$n/4] $msg" -ForegroundColor Cyan }
function Ok($msg)        { Write-Host "      $msg" -ForegroundColor Green }
function Err($msg)       { Write-Host "`nERROR: $msg" -ForegroundColor Red; Read-Host "Press Enter to exit"; exit 1 }

Write-Host ""
Write-Host "============================================" -ForegroundColor Yellow
Write-Host "  VoiceDictation Updater" -ForegroundColor Yellow
Write-Host "============================================" -ForegroundColor Yellow

# ---- Step 1: Pull latest source ----
Step 1 "Getting latest source from GitHub..."
if (Test-Path "$WorkDir\.git") {
    Set-Location $WorkDir
    git pull origin main
    if ($LASTEXITCODE -ne 0) { Err "git pull failed." }
} else {
    if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force }
    git clone $RepoUrl $WorkDir
    if ($LASTEXITCODE -ne 0) { Err "git clone failed. Is git installed? https://git-scm.com/download/win" }
}
Ok "Source up to date."

# ---- Step 2: Build ----
Step 2 "Building..."
Set-Location $WorkDir
$DistDir = "$WorkDir\dist"
dotnet publish -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableWindowsTargeting=true `
    -o $DistDir
if ($LASTEXITCODE -ne 0) { Err "Build failed. Is .NET 8 SDK installed? https://dotnet.microsoft.com/en-us/download/dotnet/8.0" }
Ok "Build succeeded."

# ---- Step 3: Test ----
Step 3 "Launching new version for testing..."
$DistExe = "$DistDir\VoiceDictation.exe"
Copy-Item "$InstallDir\groq_key.txt" "$DistDir\groq_key.txt" -Force -ErrorAction SilentlyContinue

# Copy settings too so the test version feels familiar
Copy-Item "$InstallDir\settings.json" "$DistDir\settings.json" -Force -ErrorAction SilentlyContinue

Start-Process $DistExe
Write-Host ""
Write-Host "  The new version is running from a temp folder." -ForegroundColor White
Write-Host "  Your installed version is untouched." -ForegroundColor White
Write-Host ""
$happy = Read-Host "  Happy with it? Kill the test version, then type Y to install, N to abort"

if ($happy -ne 'Y' -and $happy -ne 'y') {
    Write-Host "Aborted. Killing test instance..." -ForegroundColor Yellow
    Get-Process VoiceDictation -ErrorAction SilentlyContinue | Stop-Process -Force
    Read-Host "Press Enter to exit"
    exit 0
}

# ---- Step 4: Install ----
Step 4 "Installing..."
Write-Host "  Stopping running instance..."
Get-Process VoiceDictation -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

Write-Host "  Backing up old version..."
if (Test-Path "$InstallDir\VoiceDictation.exe") {
    Copy-Item "$InstallDir\VoiceDictation.exe" "$InstallDir\VoiceDictation.exe.bak" -Force
}

Write-Host "  Copying new version..."
Copy-Item $DistExe "$InstallDir\VoiceDictation.exe" -Force
if (-not $?) { Err "Failed to copy exe. Check the install folder: $InstallDir" }

Write-Host "  Restarting VoiceDictation..."
Start-Process "$InstallDir\VoiceDictation.exe"

Ok "VoiceDictation updated and restarted!"

Read-Host "`nPress Enter to exit"
