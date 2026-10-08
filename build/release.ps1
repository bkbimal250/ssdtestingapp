param([string]$InnoSetupPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    & (Join-Path $PSScriptRoot 'verify.ps1') -Ui
    if ($LASTEXITCODE -ne 0) { throw 'Verification failed; packaging stopped.' }

    $publish = Join-Path $root 'artifacts\publish\win-x64'
    if (Test-Path $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
    dotnet publish pixinit\pixinit.csproj -c Release --artifacts-path artifacts/diagnostics-build -warnaserror -r win-x64 --self-contained true -o $publish -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }
    if (-not (Test-Path (Join-Path $publish 'PIXINIT.exe'))) { throw 'Published PIXINIT.exe is missing.' }

    $forbidden = Get-ChildItem $publish -Recurse -File | Where-Object { $_.Extension -in '.pdb','.cs','.csproj' -or $_.Name -match '(test|sample|secret)' }
    if ($forbidden) { throw "Development files entered the publish output: $($forbidden.FullName -join ', ')" }

    if (-not $InnoSetupPath) {
        $candidates = @((Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'), 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe', 'C:\Program Files\Inno Setup 6\ISCC.exe')
        $InnoSetupPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    }
    if (-not $InnoSetupPath -or -not (Test-Path $InnoSetupPath)) { throw 'Inno Setup 6 compiler not found. Install JRSoftware.InnoSetup or pass -InnoSetupPath.' }

    & $InnoSetupPath (Join-Path $root 'installer\pixinit.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $installer = Join-Path $root 'artifacts\installer\PIXINIT-Setup-1.0.0-win-x64.exe'
    $hash = Get-FileHash $installer -Algorithm SHA256
    "$($hash.Hash)  $(Split-Path $hash.Path -Leaf)" | Set-Content (Join-Path $root 'artifacts\installer\SHA256SUMS.txt') -Encoding ascii
    $hash | Format-List Algorithm,Hash,Path
}
finally { Pop-Location }
