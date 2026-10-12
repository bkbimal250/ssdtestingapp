param([long]$LimitBytes = 100000000)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root 'artifacts\publish\win-x64'
$installer = Join-Path $root 'artifacts\installer\PIXINIT-Setup-1.0.0-win-x64.exe'
if (-not (Test-Path -LiteralPath $publish) -or -not (Test-Path -LiteralPath $installer)) { throw 'Build the release before checking size.' }
if (-not (Test-Path -LiteralPath (Join-Path $publish 'PIXINIT.exe'))) { throw 'Published PIXINIT.exe is missing.' }
$publishedBytes = (Get-ChildItem -LiteralPath $publish -Recurse -File | Measure-Object -Property Length -Sum).Sum
$installerBytes = (Get-Item -LiteralPath $installer).Length
foreach ($item in @(@{ Name='Published application'; Bytes=$publishedBytes }, @{ Name='Installer'; Bytes=$installerBytes })) {
    $status = if ($item.Bytes -lt $LimitBytes) { 'PASS' } else { 'OVER LIMIT' }
    '{0}: {1}: {2:N2} MB / {3:N2} MiB ({4} bytes)' -f $status, $item.Name, ($item.Bytes / 1000000), ($item.Bytes / 1MB), $item.Bytes
}
if ($publishedBytes -ge $LimitBytes -or $installerBytes -ge $LimitBytes) { exit 1 }
'PASS: Both artifacts are below 100 MB.'
