param([string]$InnoSetupPath)

$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'verify.ps1') -Ui
if ($LASTEXITCODE -ne 0) { throw 'Verification failed; packaging stopped.' }

$releaseScript = Join-Path (Split-Path $PSScriptRoot -Parent) 'scripts\build-release.ps1'
if ($InnoSetupPath) { & $releaseScript -InnoSetupPath $InnoSetupPath }
else { & $releaseScript }
if ($LASTEXITCODE -ne 0) { throw 'Signed release build failed.' }

$installerDir = Join-Path (Split-Path $PSScriptRoot -Parent) 'release\installer'
foreach ($required in @('PIXINIT-Setup-1.0.0-win-x64.exe', 'PIXINIT-Certificate.cer', 'SHA256.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $installerDir $required))) {
        throw "Signed release output missing: $required"
    }
}
