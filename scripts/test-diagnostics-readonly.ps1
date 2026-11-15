$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
$testDll = Join-Path $projectRoot 'artifacts/diagnostics-build/bin/pixinit.Tests/release/pixinit.Tests.dll'
$log = Join-Path $projectRoot 'release/elevated-read-only-diagnostics.log'
& 'C:\Program Files\dotnet\dotnet.exe' $testDll --diagnostics-hardware-only *> $log
$result = $LASTEXITCODE
"Exit code: $result" | Add-Content -LiteralPath $log
exit $result
