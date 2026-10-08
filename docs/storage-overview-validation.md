# Storage Overview Validation - 2026-10-07

## Changes
- Shared DeviceViewModel properties are inherited by both SATA and NVMe: Volumes, SelectedVolume, TotalStorageBytes, UsedStorageBytes, FreeStorageBytes and UsedPercent. A partial class keeps this presentation logic separate from diagnostics.
- WindowsDiskDiscovery already uses GetVolumePathNamesForVolumeNameW and associates returned mount paths with disk extents. It retains C:\ and other drive-letter roots. No transport or discovery rewrite was required.
- Usage selects a mapped drive-letter root and reads DriveInfo.TotalSize and AvailableFreeSpace off the UI thread. Used = Total - AvailableFreeSpace; percentage = Used / Total x 100. Selection-generation checks reject late results.
- Only drive-letter roots are selectable. A mounted folder is not converted into its host drive root, which would report the wrong filesystem volume. Mounts without drive letters and inaccessible volumes retain physical capacity with unavailable usage.
- No mapped volume: No mounted volume - showing device capacity only. Used/free and the progress bar remain unavailable, never zero-filled.
- Volume totals are filesystem capacity, not physical disk capacity. Available free space is user-available space; quotas may affect used = total - available. A spanned volume is not attributed wholly to one SSD.
- FirstPageView is the default native WPF Overview for both protocols. It has white cards, #E5E7EB borders, 6 px corners, Segoe UI, Consolas capacity values, GiB/GB units and a two-column/eight-row information grid.
- All sixteen fields retain availability. Identify/controller identity is preferred when present, physical discovery capacity remains distinct, and identity discrepancies remain in the warning/evidence views. Full serial remains visible.
- SATA host writes and power-on hours keep their unverified SMART-unit disclosure. SATA power cycles, host reads, spare and percentage used remain N/A without a verified existing interpretation. NVMe Critical Warning is not fabricated for SATA.
- Idle/load temperatures remain N/A without known observation conditions. Missing host writes/observed temperature offer Run Diagnostics, bound to the existing read-only commands and operation gate.
- WebView2 setup is unchanged. The existing HTML/native diagnostic overview remains in a collapsed Additional diagnostic overview section.
- Other Devices tab removed. Unidentified-device information remains accessible in a Diagnostics expander and opens on unknown-only discovery.
- Repaired an existing stray closing brace in BenchmarkPolicy that placed its free-space validation outside Validate. Preserved the current detailed budget message and limits.

## Verification
- Release build: 0 warnings, 0 errors.
- Full applicable executable suite: 288 unique checks, comprising 269 behavior checks and 19 WPF checks. This includes the original 279 coverage plus nine volume tests; two old assertions were updated for the authorized tab removal/new location.
- Volume tests use injected capacity readers, not physical writes. They cover multiple/deduplicated roots, exact counters/percentage, GiB vs GB, switching, unmapped selection, capacity-only fallback, read failure, late selection result and both protocol layouts.
- Existing bounded sustained tests retain 1 MiB disposable budgets. No full physical write benchmark or additional diagnostic reads were run.

```powershell
dotnet build pixinit.slnx -c Release --artifacts-path artifacts/diagnostics-build
dotnet artifacts/diagnostics-build/bin/pixinit.Tests/release/pixinit.Tests.dll --ui
```

- Logs: artifacts/verification/storage-overview-build.log and storage-overview-tests.log. Final test exit code: 0.
- Actual WPF unavailable-discovery fixture screenshots regenerated at 1366x768 and 1800x1000: docs/screenshots/post-phase7-nvme-1366x768.png, post-phase7-nvme-desktop.png, sata-desktop.png and sata-minimum.png. Desktop Overview was visually inspected. These are real UI renderings, not measured drive data.
- Real hardware mapping/usage, quotas, spanned volumes, physical SATA, 125/150 percent scaling and screen reader: NOT RUN. Dedicated WebView2 runtime validation remains pending; the collapsed browser is not exercised by this native Overview validation.
- No new NuGet. Manifest, WebView2 setup, benchmark safeguards and history schema unchanged.

## Changed files
- pixinit/ViewModels/Shared/DeviceViewModel.cs
- pixinit/ViewModels/Shared/DeviceStorageViewModel.cs
- pixinit/ViewModels/Sata/SataViewModel.cs
- pixinit/ViewModels/Nvme/NvmeViewModel.cs
- pixinit/ViewModels/Shell/ShellViewModel.cs
- pixinit/Views/Shared/FirstPageView.xaml and .xaml.cs
- pixinit/Views/Sata/SataView.xaml
- pixinit/Views/Nvme/NvmeView.xaml
- pixinit/MainWindow.xaml
- pixinit/Core/Benchmark/BenchmarkPolicy.cs: compile repair only
- tests/pixinit.Tests/Program.cs, DiscoveryTests.cs and StorageOverviewTests.cs
- PROJECT_SUMMARY_FOR_AI.md, docs/ssd-specifications-validation.md, this report and regenerated screenshots
- Rebuilt installer and SHA256SUMS.txt after final verification

## Installer
- Rebuilt unsigned self-contained Windows x64 installer after passing final checks: artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe.
- SHA-256: C52B0ED81464AE5D53691F1AB08BB5BEF3205AB76A2B8E23EC2941FBFD209AF1.
- Publish/compiler logs: artifacts/verification/storage-overview-publish.log and storage-overview-installer.log.
- Installation, installed launch and uninstall: NOT RUN. No distribution or Git history deletion.

## Benchmark diagnostic context and sustained averages - 2026-10-07

- Benchmark startup now awaits existing protocol-specific read-only diagnostics when the selected drive has no last snapshot. Manual constructor composition supplies async callbacks to BenchmarkViewModel; it does not invoke an async-void command and assume completion.
- Diagnostics run through their existing shared StorageOperationGate coordinators before the benchmark acquires its lease. UI remains responsive; duplicate starts are blocked during PreparingDiagnostics. Cancellation and shutdown await preparation, and target/configuration/device/consent/free-space are revalidated before writes.
- Snapshot host writes and observed temperature attach to the session only when the benchmark target reliably matches the selected drive. Ambiguous or different target mappings never inherit another drive's telemetry. Saved context persists with the full session JSON.
- Missing cards say Diagnostics not yet queried - Run Diagnostics. Run Diagnostics Now switches the workspace to Diagnostics using the selected protocol; existing read controls remain authoritative. Missing or failed telemetry remains unavailable, never fabricated.
- Completed SustainedOverallMBs is total measured bytes / total actual measured seconds / 1,000,000. Samples also provide a provisional byte/actual-interval weighted overall during progress, and a fallback for historical sessions lacking an operation result.
- SustainedPostDropMBs remains optional. The legacy persisted SustainedWriteMBs retains its post-drop semantics to preserve existing history. Overall and post-drop are exported independently. No qualifying drop reports No drop qualified - showing overall average. Cancelled measurements retain their incomplete status even when partial throughput is available.
- FirstPageView remains the default SATA/NVMe Overview. DeviceStorageViewModel still uses asynchronous DriveInfo.TotalSize and AvailableFreeSpace, calculates Total - Free and rejects stale selection results.
- Confirmed ordinary cap remains 3,221,225,472 bytes (3 GiB), policy v4. A 1024 MiB Quick workload with writes/random passes arithmetic validation. Sustained retains separate 32 GiB cap and 1 GiB circular footprint.
- Final requested Release build: 0 warnings, 0 errors. Full --ui suite: all 288 unique checks passed, exit 0. Existing assertions now also verify overall/post-drop separation, JSON/export preservation, asynchronous gated diagnostic preparation, cancellation, snapshot reuse and diagnostics navigation.
- Diagnostic preparation test uses injected snapshots and a disposable 1 MiB sustained budget. No full physical benchmark or additional hardware diagnostic reads were executed for validation.
- Actual WPF fixture screenshot: docs/screenshots/phase7-responsive-desktop.png was inspected. Missing-context wording, navigation button and separate sustained lines render. Fixtures are not measured hardware telemetry; DPI, physical SATA, real diagnostic-refresh hardware behavior and installer installation remain unverified.
- Logs: artifacts/verification/benchmark-context-build.log, benchmark-context-tests.log, benchmark-context-publish.log and benchmark-context-installer.log.
- Changed: pixinit/ViewModels/Benchmark/BenchmarkViewModel.cs; pixinit/ViewModels/Shell/ShellViewModel.cs; pixinit/MainWindow.xaml and .xaml.cs; pixinit/Core/Benchmark/BenchmarkModels.cs; pixinit/Infrastructure/Benchmarking/SustainedWriteEngine.cs; pixinit/Application/Reporting/BenchmarkReportExporter.cs; pixinit/Views/Benchmark/BenchmarkView.xaml; tests/pixinit.Tests/StorageSpecsTests.cs; tests/pixinit.Tests/BenchmarkContextTests.cs; summary/reports; regenerated screenshots and installer.
- No new NuGet. Manifest, WebView2 setup and dependency-file hashes match the prior baseline. Evidence assessment and raw providers remain separate and unchanged.

- Latest diagnostic-context installer: artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe; SHA-256 CD106900FE26B2992BDEACE6719D9981378072EE039EEF8D7DE469D9DD0A6A99. Unsigned self-contained Windows x64; rebuilt after final verification; installation/launch/uninstall NOT RUN.

## Current company workflow pass - 2026-10-07
- Supersedes earlier navigation, policy-version and test-count descriptions: two main tabs, history action, benchmark-policy-v3 with SafetyPolicy compatibility fingerprint, 304 unique passing checks and 0-warning Release build.
- Added evidence-aware idle/load capture, unidentified-device usage presentation, company TXT/JSON export and separate sustained overall/post-drop wording. No real physical benchmark was run.
- Full changed areas, commands, actual screenshot evidence, installer hash and unverified configurations: [Company workflow validation](company-workflow-validation.md).
- Universal hardware/filesystem support and production readiness remain unverified. Missing diagnostic data is retained honestly; see capture actions and limitations in the linked report.
## Latest Details pass - 2026-10-07
- SATA and NVMe Details now use white #E5E7EB cards, 6 px corners, 12 px padding, compact Segoe UI headings and Consolas values. Details expanders start expanded; long provenance remains accessible in bounded scrollable sections.
- NVMe: request outcome grid and availability dots, eight exact counter rows in a wrapping virtualized DataGrid, individual thermal status chips and eight sensor cards, composite temperature/time independent of sensor counts, controller identity grid, namespace scope disclosures and error summary grid.
- Values come from existing parsed diagnostics. Example Intel identity, temperature and thresholds are not hardcoded. Raw sensor zero means not implemented; missing fields remain N/A with reasons. Thresholds come from Identify Controller, not Namespace Identify. NVMe version is not PCIe link speed; namespace count is not physical drive count.
- SATA: sixteen identity/telemetry rows in a two-column grid plus expanded ATA, endurance, interface, discovery and assessment evidence. Full serial remains visible; providers and health assessment rules are unchanged.
- Final Release build: 0 warnings, 0 errors. All 309 unique checks passed (285 behavior, 24 WPF), including missing-data/device-switch and exact 128-bit counter checks. Logs: artifacts/verification/details-build.log and details-tests.log.
- Actual WPF client-render screenshots: docs/screenshots/details-nvme-1366x768.png, details-nvme-1800x1000.png and details-nvme-thermal-desktop.png. These use clearly named SYNTHETIC test fixtures, not physical hardware readings. Host DPI is 96; 125/150 percent, mixed DPI, screen reader and real SATA checks remain NOT RUN.
- No new NuGet, no hardware reads and no full write benchmark for this UI validation. Protected manifest/WebView2/dependency hashes unchanged. Current installer evidence will follow below.
- Latest Details installer: `artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe`; SHA-256 `D584C1E8040A3A27E9D6F15D3164F74B8A515A37E85594AD8002E4372149BB46`. Unsigned self-contained Windows x64; compile passed; install/launch/uninstall NOT RUN. This supersedes prior installer hashes; not published or distributed.

## Latest unidentified-area cleanup - 2026-10-07
- Removed OtherDeviceViewModel's FirstPageView from the unidentified inventory. Storage Usage now renders only in the main SATA/NVMe Overview, including its own no-selection placeholder.
- The gray empty header is Other / Unidentified with count. It is collapsed after empty discovery and expanded after discovery with any unidentified devices, including mixed known/unknown inventories. Device model, reported bus/reason, metadata and protocol uncertainty remain accessible.
- Main tabs remain Diagnostics and Benchmark. No providers, manifest, WebView2 or NuGet changes.
- Release: 0 warnings, 0 errors; all 310 unique checks passed. Added a WPF assertion that the unidentified area contains no FirstPageView. Fixed an existing cleanup harness race by waiting for its completion status before creating the next fixture; production cleanup code unchanged.
- Logs: artifacts/verification/unidentified-build.log and unidentified-tests.log. Existing UI fixture captures regenerated. Installer not rebuilt for this focused source/UI pass; the prior installer does not include this change.
## Latest Other / Unidentified tab - 2026-10-07
- User-requested navigation supersedes the previous two-tab layout: Diagnostics, Benchmark, Other / Unidentified, in that order.
- Moved the unidentified inventory out of Diagnostics into its own third tab. White inventory card keeps count, protocol explanation, selectable devices and reported metadata. Empty list borders are hidden; no duplicate Storage Usage is included.
- Release build: 0 warnings/errors. All 310 checks pass. Logs: artifacts/verification/other-tab-build.log and other-tab-tests.log. Actual WPF fixture screenshot: docs/screenshots/other-unidentified-tab-desktop.png.
- Providers, write safeguards, manifest and WebView2 unchanged. Existing installer predates these navigation changes; not rebuilt in this focused UI pass.
## Current installer build - 2026-10-07
- Includes latest Diagnostics, Benchmark and Other / Unidentified tab layout.
- Release: 0 warnings/errors, all 310 checks passed. Windows x64 self-contained publish and Inno Setup compilation passed.
- Installer: `artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe`. SHA-256: `0EA1882ACEC0DC5D33DABBD2A84499AEC439C18104D69CB7DF6A3B48885C8021`. Supersedes previous installer hashes.
- Unsigned. Not installed or distributed by this build task. Logs: artifacts/verification/current-installer-*.log.

## Latest benchmark alignment pass - 2026-10-08
- Benchmark configuration controls have consistent vertical alignment. Start/Cancel and status use fixed button columns with wrapping status text. Write-limit evidence remains available in an expandable section.
- Five equal-width result cards stretch across the available width instead of leaving unused desktop space. Friendly workload headings, spacing, mono values and wrapped sustained text retain all existing bindings.
- Idle/load labels and capture buttons use shared columns; TBW and thermal cards retain their independent evidence and reference disclosures. Samples now show seconds/MB/s explicitly and an honest empty-state message.
- Only BenchmarkView.xaml and the corresponding existing UI-heading assertion changed. Benchmark engine, limits, gate, consent, providers, history, manifest and WebView2 setup unchanged. No physical benchmark run.
- Release build: 0 warnings/errors; all 310 checks passed. Logs: artifacts/verification/benchmark-layout-build.log and benchmark-layout-tests.log. Actual WPF fixture captures inspected at 1366 x 768 and 1800 x 1000: docs/screenshots/phase7-responsive-1366x768.png and phase7-responsive-desktop.png. These are empty-state fixtures at 96 DPI, not measured hardware or proof of DPI support.
- Installer was not rebuilt for this focused UI pass; the previous installer predates these changes.
## Raw Data navigation removal - 2026-10-08
- Removed Raw Data tab from SATA and NVMe views at user request. Both now expose Overview, SMART and Details only. Raw diagnostic models, history and exports are retained.
- Release build passed with 0 warnings/errors; all 310 checks passed. Existing navigation checks updated; logs: artifacts/verification/remove-raw-tab-build.log and remove-raw-tab-tests.log.
- Installer not rebuilt for this UI-only pass; existing installer predates this change.
## SMART alignment and responsive layout - 2026-10-08
- Balanced NVMe SMART columns across field, reported value and meaning; exact values use Consolas and wrap within the available width. White bordered table and consistent cell/header padding align the data.
- Shared data grids now use automatic row height with a 36 px minimum so wrapped counters and explanations are not clipped. Keyboard selection and row/column virtualization remain enabled.
- NVMe SMART uses a bounded table inside a vertically scrollable minimum-height layout, preventing the laptop header from consuming the entire data viewport. Warning/source evidence remains reachable. SATA keeps its distinct attribute columns, wider raw-value column, horizontal overflow and improved empty-state position.
- Release: 0 warnings/errors; all 310 checks passed. Existing minimum-width SATA virtualization and keyboard checks passed. Logs: artifacts/verification/smart-layout-build.log and smart-layout-tests.log.
- Actual WPF fixture captures: docs/screenshots/smart-nvme-1366x768.png and smart-nvme-1800x1000.png. Synthetic readings are explicitly labeled; no physical diagnostic or benchmark run. DPI scaling, screen reader and broader hardware compatibility remain unverified.
- Manifest, WebView2, provider logic and measurement calculations unchanged. Existing installer predates this UI pass and was not rebuilt.