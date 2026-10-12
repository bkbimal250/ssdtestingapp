# Lightweight pure WPF release

## Changes

- Application and test projects target net8.0-windows. Microsoft.Data.Sqlite 10.0.12 remains the only direct NuGet package. Removed Microsoft.Web.WebView2, WebOverview.cs, Overview.html, the embedded HTML resource, WebOverviewTests.cs and obsolete --web-ui harness mode. Existing native FirstPageView remains both protocol overviews. No Chrome, CEF, Electron or HTML assets in the package.
- Enabled compressed single-file publishing, native-library self-extraction, no ReadyToRun/symbols, English satellite resources and normal globalization. Kept trimming off: WPF trimming is unsupported (NETSDK1168). No duplicate-output warnings suppressed.
- NuGet source mapping permits .NET 8 runtime/build packs and SQLite's existing transitive System.Memory dependency. No new application PackageReference.
- release.ps1 uses the existing project path, verifies tests first, checks deletion target containment, publishes self-contained win-x64 and refuses multiple files or output at least 100,000,000 bytes before packaging. size-check.ps1 also requires PIXINIT.exe and enforces both folder/installer sizes.
- Inno Setup lzma2/ultra64, solid compression; only PIXINIT.exe packaged. Original installer filename retained as explicitly requested. No source, tests, symbols or browser loaders shipped.

## Actual verification

- Release build: 0 warnings, 0 errors. All 382 unique applicable checks passed, including 30 WPF checks and the simulated 1 NVMe + 2 SATA inventory/selection checks. Removed browser-specific opt-in tests were not part of that 382-check suite.
- Logs: artifacts/verification/lightweight-build.log, lightweight-release.log, lightweight-size.log.
- Published folder contains exactly one PIXINIT.exe: 67,257,372 bytes (67.26 MB / 64.14 MiB). PASS below decimal 100 MB.
- Installer: artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe; 62,315,131 bytes (62.32 MB / 59.43 MiB). PASS below 100 MB; exceeds the aspirational 45-55 MB target because the embedded bundle is already compressed.
- SHA-256: 19E4BA2FA9385B790D0A1CF5EB6DD414D7B0FE126096AF41C5D03B8E8FEEA8F9. Authenticode NotSigned.
- Actual published executable launched with administrator consent on Windows 10 Pro; process 1860 opened the PIXINIT storage diagnostics window. Normal read-only startup discovery completed: 1 disk, 1 NVMe, 0 SATA. TEMP PIXINIT_detect.log records completion at 2026-10-12 13:14:56 local. No physical benchmark or additional health read was run.
- CloseMainWindow request succeeded; complete elevated shutdown not independently confirmed. WPF harness lifecycle checks passed.
- SelfContained=true and embedded Windows x64 .NET 8 runtime verified by the successful publish. No separate runtime install is requested. This host also has .NET installed, so isolated runtime-free machine verification remains NOT RUN.

## Limits

- Compression reduces delivered/published size, not total memory or runtime storage. Native libraries extract to the .NET TEMP cache; total runtime footprint may exceed 100 MB.
- Fresh installer installation, uninstall and upgrade from the older multi-file package NOT RUN. Previous DLL/browser-cache files may remain on upgrade; no arbitrary directory cleanup added.
- Physical Windows 11 1 NVMe + 2 SATA, other controllers, 125/150 percent DPI, mixed DPI and screen reader NOT RUN. Simulated inventory does not establish controller compatibility.
- Example Best 1696 MB/s, 17.778/100 TB and 37.9 C are not fabricated or asserted. No performance run was authorized for this packaging verification. Existing observed values and model-specific endurance evidence remain unchanged.

## Reproduce

```powershell
cd "C:\Users\ADMIN\Desktop\Bimal Works\Bimal Works\SSD\pixinit"
.\build\release.ps1
.\build\size-check.ps1
```

- Release readiness: PARTIAL until installer lifecycle and Windows 11 client hardware verification.
