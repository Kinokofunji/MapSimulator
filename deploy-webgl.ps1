# deploy-webgl.ps1
# One-click: rebuild Unity WebGL, copy into simulator-web/public/Build, and help commit + push.
# Usage: run  .\deploy-webgl.ps1  from PowerShell.
#
# Why this script exists: several past deployments showed the old site because a manual step
# was skipped (forgot to copy the build output, committed nothing, built the wrong scene...).
# A fixed script removes that class of mistake.
#
# Note: this file is intentionally plain ASCII only (no non-English characters at all).
# Windows PowerShell 5.1 does not reliably auto-detect UTF-8 without a BOM, and a script
# containing non-ASCII text without a BOM can fail to parse with confusing "missing '}'"
# errors. Sticking to ASCII sidesteps that problem entirely.

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$unityProjectPath = Join-Path $repoRoot "MapSimulator_3D"
$unityExe = "C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe"
$stagingFolder = Join-Path $unityProjectPath "Build"
$targetBuildFolder = Join-Path $repoRoot "simulator-web\public\Build"
$logFile = Join-Path $unityProjectPath "webgl_build.log"

if (-not (Test-Path $unityExe)) {
    Write-Error "Unity Editor not found at: $unityExe`nIf it is installed elsewhere, edit the `$unityExe path near the top of this script."
    exit 1
}

Write-Host "=== 1. Clearing old build staging folder ===" -ForegroundColor Cyan
if (Test-Path $stagingFolder) {
    Remove-Item -LiteralPath $stagingFolder -Recurse -Force
}

Write-Host "=== 2. Building WebGL via Unity batch mode (this can take a few minutes) ===" -ForegroundColor Cyan
& $unityExe -batchmode -quit -nographics `
    -projectPath $unityProjectPath `
    -executeMethod WebGLBuildScript.BuildForDeploy `
    -logFile $logFile

$exitCode = $LASTEXITCODE

Write-Host "--- last 30 lines of the build log ---" -ForegroundColor DarkGray
Get-Content $logFile -Tail 30

# Trust the actual build output over PowerShell's captured exit code.
# In practice $LASTEXITCODE has come back blank/nonzero even when Unity's own log clearly
# shows "BuildForDeploy" succeeded and wrote real files -- something in how this Unity
# version signals its exit code isn't reaching PowerShell cleanly. Checking for the real
# output file is the only evidence that actually proves whether the build worked.
#
# There's also a real race here, confirmed by testing: Unity's log says "build succeeded" and
# the process exits, but the WebGL toolchain (a separate bee_backend/emscripten process tree)
# keeps flushing Build.wasm to disk in the background for a while after that -- observed taking
# more than 10 seconds in practice. So: wait generously, AND require the file size to be stable
# across two checks in a row (not still growing / mid-write) before trusting it.
$wasmSourcePath = Join-Path $stagingFolder "Build\Build.wasm"
$foundWasm = $false
$lastSize = -1
$stableCount = 0

Write-Host "Waiting for $wasmSourcePath to finish being written (up to 2 minutes)..." -ForegroundColor DarkGray

for ($i = 0; $i -lt 60; $i++) {
    if (Test-Path $wasmSourcePath) {
        $currentSize = (Get-Item $wasmSourcePath).Length
        if ($currentSize -eq $lastSize -and $currentSize -gt 0) {
            $stableCount++
            if ($stableCount -ge 2) {
                $foundWasm = $true
                break
            }
        }
        else {
            $stableCount = 0
        }
        $lastSize = $currentSize
    }
    Start-Sleep -Seconds 2
}

if (-not $foundWasm) {
    Write-Error "Unity build did not produce a stable $wasmSourcePath after waiting 2 minutes (Unity process exit code was: '$exitCode'). Check the log: $logFile"
    exit 1
}

if ($exitCode -ne 0) {
    Write-Host "Note: Unity's reported exit code was '$exitCode' (not 0), but $wasmSourcePath exists and looks like a real, fresh build output, so continuing anyway." -ForegroundColor Yellow
}

Write-Host "=== 3. Copying build output into simulator-web/public/Build ===" -ForegroundColor Cyan
if (-not (Test-Path $targetBuildFolder)) {
    New-Item -ItemType Directory -Path $targetBuildFolder -Force | Out-Null
}

Copy-Item (Join-Path $stagingFolder "Build\*") $targetBuildFolder -Recurse -Force
Copy-Item (Join-Path $stagingFolder "TemplateData") $targetBuildFolder -Recurse -Force
Copy-Item (Join-Path $stagingFolder "index.html") $targetBuildFolder -Force

Write-Host "Copy done." -ForegroundColor Green

Write-Host "=== 4. Git status (confirming the build files actually changed) ===" -ForegroundColor Cyan
Set-Location $repoRoot
git status --short simulator-web/public/Build

$changed = git status --porcelain simulator-web/public/Build
if ([string]::IsNullOrWhiteSpace($changed)) {
    Write-Host "No changes under simulator-web/public/Build -- identical to what is already deployed, nothing to commit." -ForegroundColor Yellow
    exit 0
}

$confirm = Read-Host "The changes above are about to be committed and pushed. Continue? (y/N)"
if ($confirm -ne "y") {
    Write-Host "Cancelled. Changes are left uncommitted in your working tree." -ForegroundColor Yellow
    exit 0
}

git add simulator-web/public/Build
git commit -m "update WebGL build"
git push

Write-Host "=== Done! Vercel should auto-detect the new commit and redeploy ===" -ForegroundColor Green
