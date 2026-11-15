param([string]$InnoSetupPath)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectFile = Join-Path $projectRoot 'pixinit\pixinit.csproj'
$manifestFile = Join-Path $projectRoot 'pixinit\app.manifest'
$innoScript = Join-Path $projectRoot 'installer\pixinit.iss'
$releaseDir = Join-Path $projectRoot 'release'
$publishDir = Join-Path $releaseDir 'publish'
$installerDir = Join-Path $releaseDir 'installer'
$installerFile = Join-Path $installerDir 'PIXINIT-Setup-1.0.0-win-x64.exe'
$certificateFile = Join-Path $projectRoot 'pixinit.pfx'
$publicCertificateFile = Join-Path $installerDir 'PIXINIT-Certificate.cer'
$certificatePassword = 'Pixinit@123'

function Assert-ReleasePath([string]$path) {
    $absolute = [IO.Path]::GetFullPath($path)
    $prefix = $projectRoot.TrimEnd('\') + '\'
    if (-not $absolute.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Release path is outside the project: $absolute"
    }
}

function Get-PeMachine([string]$path) {
    $stream = [IO.File]::OpenRead($path)
    try {
        $reader = [IO.BinaryReader]::new($stream)
        $stream.Position = 0x3c
        $peOffset = $reader.ReadInt32()
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) { throw "Invalid PE signature: $path" }
        return $reader.ReadUInt16()
    }
    finally { $stream.Dispose() }
}

foreach ($path in @($releaseDir, $publishDir, $installerDir)) { Assert-ReleasePath $path }
if (-not (Test-Path -LiteralPath $projectFile)) { throw "Project missing: $projectFile" }
if (-not (Test-Path -LiteralPath $innoScript)) { throw "Inno script missing: $innoScript" }

[xml]$project = Get-Content -LiteralPath $projectFile -Raw
$framework = [string]$project.Project.PropertyGroup.TargetFramework
$version = [string]$project.Project.PropertyGroup.Version
$publisher = [string]$project.Project.PropertyGroup.Company
$exeName = [string]$project.Project.PropertyGroup.AssemblyName + '.exe'
if ($framework -notmatch '^net\d+\.\d+-windows$' -or $version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Unexpected target or installer version: $framework / $version"
}
if ($exeName -ne 'PIXINIT.exe') { throw "Unexpected application executable: $exeName" }
if ((Get-Content -LiteralPath $manifestFile -Raw) -notmatch 'requestedExecutionLevel level="requireAdministrator"') {
    throw 'The current installer elevation setting must be reviewed against app.manifest.'
}

if (-not $InnoSetupPath) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $candidates = @(
        $(if ($command) { $command.Source }),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    )
    $InnoSetupPath = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
}
if (-not $InnoSetupPath -or -not (Test-Path -LiteralPath $InnoSetupPath)) {
    throw 'ISCC.exe was not found. Pass -InnoSetupPath with its full path.'
}

$signTool = Get-Command signtool.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1
if (-not $signTool) {
    $kitBin = 'C:\Program Files (x86)\Windows Kits\10\bin'
    $signTool = Get-ChildItem -LiteralPath $kitBin -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
        Sort-Object FullName -Descending | Select-Object -ExpandProperty FullName -First 1
}
if (-not $signTool) { throw 'SignTool.exe was not found. Install the Windows SDK Signing Tools before packaging.' }

function Initialize-ReleaseCertificate {
    if (-not (Test-Path -LiteralPath $certificateFile)) {
        $certificate = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=PIXINIT, O=Bimal Vishwakarma' `
            -CertStoreLocation 'Cert:\CurrentUser\My' -KeyExportPolicy Exportable -KeyAlgorithm RSA `
            -KeyLength 3072 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(3)
        $securePassword = ConvertTo-SecureString $certificatePassword -AsPlainText -Force
        Export-PfxCertificate -Cert $certificate -FilePath $certificateFile -Password $securePassword | Out-Null
    }
    $loaded = [Security.Cryptography.X509Certificates.X509Certificate2]::new(
        $certificateFile, $certificatePassword, [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable)
    if (-not $loaded.HasPrivateKey -or $loaded.Subject -ne 'CN=PIXINIT, O=Bimal Vishwakarma' -or
        $loaded.NotAfter -le (Get-Date) -or
        -not (@($loaded.EnhancedKeyUsageList | ForEach-Object { $_.ObjectId })).Contains('1.3.6.1.5.5.7.3.3')) {
        $loaded.Dispose()
        throw 'pixinit.pfx is missing the requested valid PIXINIT code-signing certificate.'
    }
    [IO.File]::WriteAllBytes($publicCertificateFile,
        $loaded.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    return $loaded
}

function Sign-ReleaseFile([string]$path, [Security.Cryptography.X509Certificates.X509Certificate2]$certificate) {
    & $signTool sign /f $certificateFile /p $certificatePassword /fd SHA256 `
        /tr http://timestamp.digicert.com /td SHA256 $path
    if ($LASTEXITCODE -ne 0) { throw "SignTool signing failed: $path" }
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $verification = & $signTool verify /pa /v $path 2>&1 | Out-String
    $ErrorActionPreference = $previousPreference
    if ($LASTEXITCODE -ne 0) {
        $signature = Get-AuthenticodeSignature -LiteralPath $path
        if ($verification -notmatch 'certificate\s+which\s+is\s+not\s+trusted' -or
            $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint -or
            $null -eq $signature.TimeStamperCertificate -or
            $signature.StatusMessage -notmatch 'root\s+certificate\s+which\s+is\s+not\s+trusted') {
            throw "SignTool verification failed: $path`n$verification"
        }
        Write-Warning "Signature and timestamp are present, but this self-signed publisher is not trusted by Windows: $path"
    }
    else { Write-Host "SignTool verified: $path" }
}

New-Item -ItemType Directory -Path $releaseDir, $installerDir -Force | Out-Null
$signingCertificate = Initialize-ReleaseCertificate
if (Test-Path -LiteralPath $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

Push-Location $projectRoot
try {
    Write-Host "Building $framework Release"
    & dotnet build $projectFile -c Release -r win-x64 --self-contained true --artifacts-path (Join-Path $releaseDir 'build') -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

    Write-Host 'Publishing win-x64 self-contained single file'
    & dotnet publish $projectFile -c Release -r win-x64 --self-contained true -warnaserror -o $publishDir --property:PublishTrimmed=false --property:PublishSingleFile=true --property:IncludeNativeLibrariesForSelfExtract=true --property:EnableCompressionInSingleFile=true --property:PublishReadyToRun=false --property:DebugType=none --property:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'Release publish failed.' }

    foreach ($required in @($exeName)) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishDir $required))) { throw "Required publish file missing: $required" }
    }
    Get-ChildItem -LiteralPath $publishDir -Recurse -File |
        Where-Object { $_.Extension -in '.pdb', '.xml' } |
        Remove-Item -Force
    if ((Get-PeMachine (Join-Path $publishDir $exeName)) -ne 0x8664) { throw 'Published application is not an x64 PE executable.' }
    Sign-ReleaseFile (Join-Path $publishDir $exeName) $signingCertificate
    $publishFiles = @(Get-ChildItem -LiteralPath $publishDir -Recurse -File)
    Write-Host "Publish contains $($publishFiles.Count) files ($([math]::Round(($publishFiles | Measure-Object Length -Sum).Sum / 1MB, 2)) MiB)."

    Write-Host "Compiling PIXINIT $version with Inno Setup"
    $defines = @("/DAppVersion=$version")
    if (-not [string]::IsNullOrWhiteSpace($publisher)) { $defines += "/DAppPublisher=$publisher" }
    & $InnoSetupPath @defines $innoScript
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
    if (-not (Test-Path -LiteralPath $installerFile)) { throw "Installer missing: $installerFile" }
    Sign-ReleaseFile $installerFile $signingCertificate

    $hash = Get-FileHash -LiteralPath $installerFile -Algorithm SHA256
    "$($hash.Hash)  $(Split-Path $installerFile -Leaf)" | Set-Content -LiteralPath (Join-Path $installerDir 'SHA256.txt') -Encoding ascii
    Write-Host "Installer: $installerFile"
    Write-Host "SHA256: $($hash.Hash)"
    Write-Host 'Install target: x64 Windows; application requests administrator access for disk diagnostics.'
    Write-Host "Certificate: $publicCertificateFile"
    Write-Host 'Code signing: self-signed; clients will not trust it by default and SmartScreen warnings may remain.'
    $global:LASTEXITCODE = 0
}
finally { $signingCertificate.Dispose(); Pop-Location }
