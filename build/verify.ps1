param([switch]$Ui, [switch]$Hardware)
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    dotnet build pixinit.slnx -c Release --artifacts-path artifacts/diagnostics-build -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    $testArguments = @('artifacts/diagnostics-build/bin/pixinit.Tests/release/pixinit.Tests.dll')
    if ($Ui) { $testArguments += @('--ui') }
    & dotnet @testArguments
    if ($LASTEXITCODE -ne 0) { throw 'Verification failed' }
    if ($Hardware) {
        dotnet artifacts/diagnostics-build/bin/pixinit.Tests/release/pixinit.Tests.dll --hardware
        if ($LASTEXITCODE -ne 0) { throw 'Hardware verification failed' }
    }
}
finally { Pop-Location }
