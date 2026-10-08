# PIXINIT

Windows C#/.NET 10 WPF storage diagnostics with distinct SATA/NVMe views, SQLite history, reporting and safe bounded filesystem benchmarks.

- Main tabs: Diagnostics, Benchmark, Other / Unidentified.
- Diagnostic tabs: Overview, SMART, Details.
- Full serial display and availability-aware evidence. Buffered QD1 measurements include caching effects.
- Current release qualification: PARTIAL. Universal hardware and DPI compatibility are not established.

## Build and package

```powershell
.\build\verify.ps1 -Ui
.\build\release.ps1
```

Requires .NET 10 SDK; packaging also requires Inno Setup 6. The release script runs verification itself, so running both commands is optional. Output: `artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe`, with `SHA256SUMS.txt` beside it. The self-contained x64 installer is unsigned and requests administrator privileges.

The optional `-Hardware` verification mode includes physical reads and writes. Do not use it for layout checks.

- [Installation, upgrade, uninstall and DPI testing](docs/installation.md)
- [Current release evidence](docs/release-2026-10-08.md)
- [Compatibility matrix](docs/compatibility.md)
- [Release checklist](docs/release-checklist.md)
- [Project summary for AI](PROJECT_SUMMARY_FOR_AI.md)
- [Architecture](docs/architecture.md)
- [Storage overview and recent UI validation](docs/storage-overview-validation.md)
- [SSD specification implementation](docs/ssd-specifications-validation.md)

Historical phase reports in `docs` record earlier builds. Current release documentation takes precedence where behavior has changed.