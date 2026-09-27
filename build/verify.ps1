param([switch]$Ui, [switch]$Hardware)
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    dotnet build pixinit.slnx -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    $testArguments = @('run', '--project', 'tests/pixinit.Tests', '-c', 'Release', '--no-build')
    if ($Ui) { $testArguments += @('--', '--ui') }
    & dotnet @testArguments
    if ($LASTEXITCODE -ne 0) { throw 'Verification failed' }
    if ($Hardware) {
        dotnet run --project tests/pixinit.Tests -c Release --no-build -- --hardware
        if ($LASTEXITCODE -ne 0) { throw 'Hardware verification failed' }
    }
}
finally { Pop-Location }
