param([string]$InnoSetupPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    & (Join-Path $PSScriptRoot 'verify.ps1') -Ui
    if ($LASTEXITCODE -ne 0) { throw 'Verification failed; packaging stopped.' }

    $publish = Join-Path $root 'artifacts\publish\win-x64'
    if (-not ([IO.Path]::GetFullPath($publish).StartsWith([IO.Path]::GetFullPath($root) + '\', [StringComparison]::OrdinalIgnoreCase))) { throw 'Publish directory outside project.' }
    if (Test-Path $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
    dotnet publish pixinit\pixinit.csproj -c Release --artifacts-path artifacts/diagnostics-build -warnaserror -r win-x64 --self-contained true -o $publish -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }
    $dependencyManifest = Join-Path $root 'artifacts/diagnostics-build/bin/pixinit/release_win-x64/PIXINIT.deps.json'
    if (-not (Test-Path -LiteralPath $dependencyManifest)) { throw 'Release dependency manifest missing; packaging stopped.' }
    if (Test-Path -LiteralPath $dependencyManifest) {
        if ((Get-Content -LiteralPath $dependencyManifest -Raw) -match '(?i)(sqlite|SQLitePCLRaw)') { throw 'SQLite dependency entered release; packaging stopped.' }
    }

    if (-not (Test-Path (Join-Path $publish 'PIXINIT.exe'))) { throw 'Published PIXINIT.exe is missing.' }

    $files = @(Get-ChildItem -LiteralPath $publish -Recurse -File)
    if ($files.Count -ne 1 -or $files[0].Name -ne 'PIXINIT.exe') { throw 'Publish must contain only PIXINIT.exe.' }
    if ($files[0].Length -ge 100000000) { throw 'Published application exceeds 100 MB; packaging stopped.' }

    $forbidden = Get-ChildItem $publish -Recurse -File | Where-Object { $_.Extension -in '.pdb','.cs','.csproj' -or $_.Name -match '(test|sample|secret)' }
    if ($forbidden) { throw "Development files entered the publish output: $($forbidden.FullName -join ', ')" }

    if (-not $InnoSetupPath) {
        $candidates = @('C:\Program Files (x86)\Inno Setup 6\ISCC.exe', (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'), 'C:\Program Files\Inno Setup 6\ISCC.exe')
        $InnoSetupPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    }
    if (-not $InnoSetupPath -or -not (Test-Path $InnoSetupPath)) {
        Write-Host 'Inno Setup 6 not found - installing...'
        $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
        if (-not $winget) { throw 'Inno Setup 6 is required to build the installer, but winget.exe is unavailable.' }
        & $winget.Source install --id JRSoftware.InnoSetup -e --silent --accept-package-agreements --accept-source-agreements
        if ($LASTEXITCODE -ne 0) { throw "Inno Setup 6 installation failed through winget (exit code $LASTEXITCODE)." }
        $InnoSetupPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
        if (-not $InnoSetupPath) { throw 'Inno Setup 6 installation completed but ISCC.exe was not found in a supported installation location.' }
    }

    & $InnoSetupPath (Join-Path $root 'installer\pixinit.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $installer = Join-Path $root 'artifacts\installer\PIXINIT-Setup-1.0.0-win-x64.exe'
    if (-not (Test-Path -LiteralPath $installer)) { throw 'Inno Setup reported success but the installer executable is missing.' }
    $testingZip = Join-Path $root 'artifacts\publish\PIXINIT-1.0.0-win-x64.zip'
    if (Test-Path -LiteralPath $testingZip) { Remove-Item -LiteralPath $testingZip -Force }
    & (Join-Path $PSScriptRoot 'size-check.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Release size verification failed.' }
    $hash = Get-FileHash $installer -Algorithm SHA256
    "$($hash.Hash)  $(Split-Path $hash.Path -Leaf)" | Set-Content (Join-Path $root 'artifacts\installer\SHA256SUMS.txt') -Encoding ascii
    $hash | Format-List Algorithm,Hash,Path
}
finally { Pop-Location }
