# PIXINIT

PIXINIT is an open-source Windows utility for inspecting SSD health information and running bounded SSD performance tests. It supports NVMe and SATA devices exposed by Windows, presents the diagnostic evidence returned by the drive/controller, and keeps benchmark work limited to a user-selected test location.

## Features

- Lists the physical storage devices detected by Windows and their mounted volumes.
- Displays available model, serial, firmware, capacity, interface, health, temperature, power-on, and host read/write information.
- Supports live NVMe and SATA SMART diagnostics where the controller or USB bridge exposes them.
- Runs bounded sequential and random benchmark workloads with 500 MiB, 1 GiB, and 2 GiB test-file options.
- Uses a visible, application-owned `PIXINIT_TEST` folder on the selected volume and cleans up only files it owns.
- Clearly marks unavailable or unverified device data instead of inventing readings.
- Stores operational diagnostic logs in the current user's temporary folder; it does not require a database or cloud service.

## System requirements

- Windows 10 or Windows 11, 64-bit.
- .NET 8 SDK to build from source.
- The published Windows release is self-contained and does not require a separately installed .NET runtime.

## Administrator privileges

PIXINIT requests administrator privileges because Windows restricts low-level, read-only storage queries such as NVMe/SATA identify data and SMART diagnostics. These privileges are used to query the selected storage device and to run the selected benchmark on its mounted volume. PIXINIT does not format, sanitize, erase, or modify raw disk sectors.

Benchmarking writes only bounded test files to the user-selected mounted volume. Do not benchmark a drive containing data that has not been backed up.

## Build from source

Clone the repository and run the following commands from the repository root:

```powershell
dotnet restore pixinit.slnx
dotnet build pixinit.slnx -c Release --no-restore
dotnet publish pixinit/pixinit.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\publish
```

To run the repository's verification script locally:

```powershell
.\build\verify.ps1 -Ui
```

The installer build additionally requires Inno Setup 6:

```powershell
.\scripts\build-release.ps1
```

Generated `bin`, `obj`, `artifacts`, and `release` output is intentionally excluded from version control.

## Code Signing Policy

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

If PIXINIT is accepted into the SignPath Foundation program, release artifacts will be built by the repository's GitHub Actions workflow and submitted through the Foundation-configured SignPath project. The workflow is configured through repository variables and secrets; no signing credential or private key is stored in this repository.

## Contributing

Issues and pull requests are welcome. Please keep changes buildable on Windows, avoid committing generated binaries or credentials, and include validation details for behavior changes.

## License

Copyright (c) 2026 Bimal Vishwakarma.

PIXINIT is licensed under the [MIT License](LICENSE).
