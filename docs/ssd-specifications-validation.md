# SSD specification implementation - 2026-10-07

## Inputs and scope
- Read PROJECT_SUMMARY_FOR_AI.md, SYSTEM_DESIGN_SDE2.md and the supplied SSD Specifications (1).pdf.
- Preserve the existing session model, MVVM, C#/WPF, SQLite, owned-file cleanup and read-only diagnostic transports.
- No new NuGet added in this request. App manifest and WebView2 setup were not modified in this request.

## Behavior
| Specification | Implemented behavior | Evidence limit |
| --- | --- | --- |
| Sequential read/write | Existing 1 MiB default sequential workload, measured byte/time throughput | Buffered QD1 filesystem results |
| Random 4 KiB | Quick default 1000 I/Os; aligned random offsets, complete-block reads, per-I/O latency including positioning | Latency-derived IOPS = 1000 / average latency ms; requested Rnd*MBs fields are binary MiB/s. Original wall-clock IOPS remains distinct. Custom/Standard configurations retain configurable counts. |
| TBW | NVMe DUW x 512 x 1000 / 1e12; SATA 241 conventional LE48 x 512 / 1e12; power-on hours retained | Written TB is host-write telemetry, not manufacturer-rated TBW or NAND writes. 600 TB is an unverified requested reference. SATA 194/241/9 encodings remain vendor-dependent and are labeled unverified. |
| Sustained | Opt-in circular 1 GiB owned file, 1 MiB writes, up to 30 seconds or the selected byte cap, whichever schedules its final write first; one-second interval samples | Buffered writes; FlushAsync is not durable flush. Native I/O can overrun the scheduling deadline. A speed drop is not proof of SLC exhaustion. |
| Temperature | NVMe composite temperature and conventional SATA 194 low-byte interpretation; Green below 75 C, Yellow 75 through 80 C, Red above 80 C | Idle and load remain null without known measurement conditions. No extra diagnostic commands or automatic sampling added. |

- Ordinary workloads retain the 1 GiB application-write cap and 1024 MiB configurable footprint. Sustained testing has a separate configurable 1 MiB-32 GiB write cap, requires explicit workload consent, and is disabled by default.
- Free-space reserve covers the 1 GiB circular file plus the existing safety margin. Target, configuration, selection and available bytes are rechecked after consent.
- Benchmark work executes away from the UI thread, under the shared StorageOperationGate. Cancellation/generation handling rejects stale updates and results.
- Drop detection needs three baseline intervals and three consecutive intervals below 70 percent of baseline. The post-drop average is total bytes divided by actual interval duration; missing evidence stays unavailable.
- Five performance cards show SEQ read/write, random read/write IOPS with MiB/s, and sustained average. Separate TBW and thermal cards and a bounded simple sustained ItemsControl preserve unavailable states.
- Diagnostic context is attached to a benchmark only when the target reliably matches the selected drive. Observations are retained as snapshots, never invented load measurements.
- Existing SqliteHistoryStore already saves the entire BenchmarkSession as JSON. No DB schema rewrite was needed. New fields and samples round-trip in that JSON; text/JSON exporters include the new context.
- New benchmark policy v3 and report schema 1.1 prevent comparisons with incompatible historical methodology. Historical reports preserve their stored policy and methodology.

## Verification
- Release build: 0 warnings, 0 errors.
- Full applicable suite with --ui: 279 unique checks (260 behavior checks including 22 specification checks; 19 WPF UI checks).
- Tests cover numeric boundaries, zero/missing readings, duplicate/malformed SATA raw fields, NVMe conversion, weighted drop detection, write budgets, consent changes, cancellation, shared gate, owned-file cleanup, full JSON persistence and exports.
- Sustained failure tests write only 1 MiB per disposable test run. No full-duration physical-device benchmark was run.
- Actual WPF empty-state client renders regenerated at 1366 x 768 and 1800 x 1000. Desktop benchmark screenshot visually inspected. These are test-harness screenshots, not installed hardware validation.
- The existing WebView2 emits Chrome_WidgetWin_0 unregister error 1412 during harness teardown despite passing checks. WebView2 setup is outside this request and remains unchanged. Earlier standalone HTML-overview verification is still pending.
- Physical SATA, full sustained hardware run, confirmed post-SLC speed, verified manufacturer TBW, idle/load observations, installer installation, scaling, mixed DPI and screen-reader checks remain unverified.

## Changed application and test files
- pixinit/App.xaml.cs
- pixinit/Application/Assessment/HealthAssessor.cs
- pixinit/Application/Benchmarking/WriteConsent.cs
- pixinit/Application/Reporting/BenchmarkReportExporter.cs
- pixinit/Core/Benchmark/BenchmarkModels.cs
- pixinit/Core/Benchmark/BenchmarkPolicy.cs
- pixinit/Core/Diagnostics/Common/StorageUsage.cs
- pixinit/Core/Diagnostics/Nvme/NvmeDiagnostics.cs
- pixinit/Core/Diagnostics/Nvme/NvmeResultMapper.cs
- pixinit/Core/Diagnostics/Sata/SataDiagnostics.cs
- pixinit/Core/Diagnostics/Sata/SataResultMapper.cs
- pixinit/Core/History/DiagnosticSnapshot.cs
- pixinit/Infrastructure/Benchmarking/FileBenchmarkEngine.cs
- pixinit/Infrastructure/Benchmarking/SustainedWriteEngine.cs
- pixinit/MainWindow.xaml
- pixinit/ViewModels/Benchmark/BenchmarkViewModel.cs
- pixinit/ViewModels/Sata/SataViewModel.cs
- pixinit/ViewModels/Shell/ShellViewModel.cs
- pixinit/Views/Benchmark/BenchmarkView.xaml
- pixinit/Views/Benchmark/BenchmarkView.xaml.cs
- tests/pixinit.Tests/Phase6Tests.cs
- tests/pixinit.Tests/Phase7Tests.cs
- tests/pixinit.Tests/Program.cs
- tests/pixinit.Tests/StorageSpecsTests.cs
- docs/ssd-specifications-validation.md
- docs/screenshots/*: regenerated by the WPF harness
- artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe and SHA256SUMS.txt: rebuilt after successful build/tests

## Packaging
- Self-contained Windows x64 publish and unsigned Inno installer rebuilt after validation.
- SHA-256: C52B0ED81464AE5D53691F1AB08BB5BEF3205AB76A2B8E23EC2941FBFD209AF1. Also recorded in artifacts/installer/SHA256SUMS.txt.
- Not installed, published or distributed in this pass.

## Completion pass verification - 2026-10-07

- Baseline: 275 checks passed; final: 279 unique checks passed. Baseline and final totals overlap and must not be added.
- Final Release build: 0 warnings, 0 errors. Final commands:

```powershell
dotnet build pixinit.slnx -c Release --artifacts-path artifacts/diagnostics-build
dotnet artifacts/diagnostics-build/bin/pixinit.Tests/release/pixinit.Tests.dll --ui
```

- Logs: artifacts/verification/complete-baseline-build.log, complete-baseline-tests.log, complete-final-build.log and complete-final-tests.log.
- Additional checks verify 1024 MiB read preparation against the unchanged 1 GiB cap, rejection of excessive combined workloads, all five performance cards/file selector, and explicit unavailable thermal/reference fields.
- Existing corruption, traversal, reparse, replacement-race, consent, cancellation, short-read, JSON/report and comparison checks pass. Sustained tests remain disposable 1 MiB runs; no full physical write benchmark or new diagnostic reads were performed.
- Cleanup now uses a locked, verified Windows file handle with FileDispositionInfo deletion on close instead of deleting a path after checking it. DELETE access is required by the [Microsoft API contract](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-setfileinformationbyhandle). Manifest/ancestor/final-file reparse checks and volume/file-index identity remain mandatory; directories are removed only when empty and never recursively.
- 1024 MiB combined ordinary read/write remains deliberately invalid because read preparation counts toward the 1 GiB application-write limit. The simple UI explains this and reports rejected configurations without an unhandled async command exception.
- 600 TB remains an unverified reference. Percentage Used remains consumed endurance. SATA conventional encoding remains unverified; media classification is Not confirmed without evidence. Identity discrepancies/raw bytes and SMART scope uncertainty remain available.
- UI: simple left device list, protocol-specific four-section navigation, five performance cards, monospaced values, plain sample table, reference bar, thermal dot, collapsed historical/technical data and export redaction.
- Actual WPF client screenshots regenerated and inspected: docs/screenshots/phase7-responsive-1366x768.png, phase7-responsive-desktop.png and post-phase7-nvme-1366x768.png. SATA empty/minimum state and virtualized SMART viewport are exercised by the harness. Screenshots use an unavailable-discovery fixture, not synthetic live measurements.
- Host DPI is 96. 125/150 percent scaling, mixed DPI and screen-reader checks: NOT RUN. Manual check: Windows Settings > System > Display > Scale, choose 125 or 150 percent, reopen PIXINIT and verify tab focus, device selection, scrolling, exports and button access at both window sizes. Screenshot inspection at 96 DPI does not prove scaling support.
- WebView2 teardown error 1412 remains visible in the final log. Standalone HTML runtime/lifecycle verification is pending; no claim that WPF fixtures verify the HTML rendering path.
- Preserved hashes: pixinit/app.manifest, pixinit/Views/Shared/WebOverview.cs, pixinit/pixinit.csproj and NuGet.Config match artifacts/verification/complete-protected-hashes.json.
- Changed in this completion pass: pixinit/Core/Diagnostics/Common/StorageUsage.cs; pixinit/Core/Benchmark/BenchmarkPolicy.cs; pixinit/Infrastructure/Benchmarking/OwnedBenchmarkFile.cs; pixinit/ViewModels/Benchmark/BenchmarkViewModel.cs; pixinit/Views/Benchmark/BenchmarkView.xaml; pixinit/MainWindow.xaml; pixinit/Views/Shared/Overview.html; tests/pixinit.Tests/StorageSpecsTests.cs; tests/pixinit.Tests/Phase6Tests.cs; tests/pixinit.Tests/Program.cs; this report; PROJECT_SUMMARY_FOR_AI.md; regenerated WPF screenshots and installer artifacts.
- No manifest, WebView2 setup, package, provider architecture or database schema change in this pass. SYSTEM_DESIGN_SDE2.md is a proposal, not implementation evidence.
- Release readiness: PARTIAL. Physical SATA, Windows 11/other controllers/filesystems, full sustained device performance, verified SLC exhaustion, manufacturer endurance, idle/load conditions and isolated installer installation/launch/uninstall remain unverified.

- Current completion installer: artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe; SHA-256: C52B0ED81464AE5D53691F1AB08BB5BEF3205AB76A2B8E23EC2941FBFD209AF1. Self-contained win-x64, unsigned. Published and compiled after the final 279-check suite; installer install/launch/uninstall NOT RUN. Tracked installer retained; no Git history deletion or distribution.

## Latest storage overview update
- Native first-page volume usage and complete availability-aware device fields are implemented for SATA/NVMe. Other Devices tab is removed; inventory retained in Diagnostics. See docs/storage-overview-validation.md for files, commands and evidence.
- Latest final Release build: 0 warnings/errors. Full suite: 288 unique passing checks, including nine new volume checks. No full physical write benchmark or additional diagnostic reads.

- Latest storage-overview installer: artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe; SHA-256 C52B0ED81464AE5D53691F1AB08BB5BEF3205AB76A2B8E23EC2941FBFD209AF1. Unsigned/self-contained win-x64; installation and uninstall NOT RUN.

## Ordinary benchmark cap correction - 2026-10-07
- MaxOrdinaryApplicationDataWritesBytes: 3,221,225,472 bytes (3 GiB), including read preparation, sequential writes and random writes. MaximumWriteBytes remains a compatibility alias.
- 1024 MiB Quick workload with Include writes + Include Random passes: 2,151,579,648 application data bytes. Large tests validate the budget only; no actual 1024 MiB write benchmark was run.
- Ordinary reserve: max(3 GiB, available free space / 10), added to file footprint. Sustained-only reserve remains max(1 GiB, available free space / 10). Mixed workloads use the ordinary reserve.
- Sustained maximum remains separate at 32 GiB; required circular-file footprint is still 1 GiB. Consent, generation checks and owned-file cleanup are unchanged.
- Preserved detailed requested/allowed byte error and Validate brace fix. Policy version bumped to benchmark-policy-v4 to distinguish the new safety policy in history/comparisons. Schema remains 1.1.
- Corrected UI limit wording and replaced obsolete 1 GiB rejection assertions with 3 GiB bounds and acceptance coverage; unchanged total of 288 unique checks.
- Requested Release command: 0 warnings, 0 errors. Requested --ui suite: 288 checks passed, exit 0. Logs: artifacts/verification/ordinary-cap-build.log and ordinary-cap-tests.log.
- Files changed: pixinit/Core/Benchmark/BenchmarkPolicy.cs; pixinit/Views/Benchmark/BenchmarkView.xaml; tests/pixinit.Tests/Phase6Tests.cs; tests/pixinit.Tests/StorageSpecsTests.cs; PROJECT_SUMMARY_FOR_AI.md; this report; regenerated UI screenshot and installer artifacts.
- No new NuGet, manifest/WebView2 setup/provider changes, full physical write benchmark or additional diagnostic reads.

- Current installer rebuilt after cap verification: artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe; SHA-256 F85A50900677A86049C4B167E90E015EA849B1B4360B1FFC961918977E9A45D3. Unsigned; installation/launch/uninstall NOT RUN. Publish/compiler logs: artifacts/verification/ordinary-cap-publish.log and ordinary-cap-installer.log.

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