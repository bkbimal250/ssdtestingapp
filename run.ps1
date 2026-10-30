#!/usr/bin/env pwsh
<#
Simple helper script to restore, build and run the PIXINIT WPF app.
Usage: .\run.ps1
Requires: .NET 8 SDK (matching TargetFramework net8.0-windows)
#>
try {
	dotnet --version | Out-Null
} catch {
	Write-Error "dotnet CLI not found. Install .NET SDK 8.0+ from https://dotnet.microsoft.com"
	exit 1
}

Write-Host "Restoring packages..."
dotnet restore || exit 1

Write-Host "Building (Release)..."
dotnet build pixinit/pixinit.csproj -c Release || exit 1

Write-Host "Running app... (use Ctrl-C to stop)"
dotnet run --project pixinit/pixinit.csproj
