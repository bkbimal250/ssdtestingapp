# PROJECT SUMMARY FOR AI

- Updated: 2026-10-07, after the company workflow implementation and 304-check verification.
- Repository: existing PIXINIT Windows desktop application. This file describes the current local working tree, including uncommitted changes; it does not establish what is published on GitHub.
- Read this file first, then the relevant source and `docs/ssd-specifications-validation.md`. `SYSTEM_DESIGN_SDE2.md` contains a proposed design; do not assume its proposed classes, DI container or test framework are implemented.

## 1. Project Goal

- Discover Windows storage devices and present separate SATA and NVMe diagnostics.
- Show evidence-based assessment, telemetry, identity, diagnostic history and exportable reports.
- Measure sequential, random and bounded sustained filesystem performance with explicit write consent and owned-file cleanup.
- Present host writes and temperature without inventing manufacturer ratings, missing observations or hardware capability claims.

## 2. Tech Stack

| Area | Current implementation |
| --- | --- |
| Language / framework | C#, .NET 10, `net10.0-windows`, Windows only |
| UI | WPF, XAML, MVVM; local HTML/CSS/JavaScript Overview through WebView2 |
| Database | Microsoft.Data.Sqlite 10.0.12; JSON session payloads |
| Embedded browser | Microsoft.Web.WebView2 1.0.4258.31; installed WebView2 runtime required for HTML presentation; native WPF fallback |
| Serialization | System.Text.Json; enum conversion and lossless integer strings in exports |
| Windows integration | SetupAPI, DeviceIoControl, allowlisted SATA/NVMe read-only transports |
| Build / packaging | .NET SDK, `pixinit.slnx`, PowerShell scripts, Inno Setup 6; self-contained win-x64 publish |
| Tests | Custom executable harness, not xUnit/NUnit; run its executable rather than treating `dotnet test` as verification |

- No Node application, package.json, remote service or frontend build tool is used.
- No additional NuGet package was added during the latest SSD-specification pass.

## 3. Folder Structure

| Path | Purpose |
| --- | --- |
| `pixinit/` | WPF application project, startup, window, manifest and project configuration |
| `pixinit/Application/Discovery/` | Discovery orchestration |
| `pixinit/Application/Selection/` | Protocol and device selection policy |
| `pixinit/Application/Scanning/` | SATA/NVMe coordinators and shared storage-operation gate |
| `pixinit/Application/Benchmarking/` | Filesystem target mapping and write-consent flow |
| `pixinit/Application/Assessment/` | New temperature/reference-endurance presentation rules in HealthAssessor |
| `pixinit/Application/Reporting/` | Diagnostic and benchmark JSON/text export |
| `pixinit/Core/Devices/` | Device identity, discovery evidence, protocol/media/access models |
| `pixinit/Core/Diagnostics/Common/` | Availability-aware metrics, TbwInfo, ThermalInfo and thermal states |
| `pixinit/Core/Diagnostics/Sata/` | ATA identity, legacy SMART parsing, result mapping |
| `pixinit/Core/Diagnostics/Nvme/` | NVMe controller, health/error parsing, result mapping |
| `pixinit/Core/Assessment/` | Existing evidence-based diagnostic checklist assessment; distinct from the new display thresholds |
| `pixinit/Core/Benchmark/` | Configuration, operations, sessions, progress, samples, policy and comparisons |
| `pixinit/Core/History/` | Diagnostic snapshots, identity matching and metric comparisons |
| `pixinit/Core/Abstractions/` | Provider abstractions |
| `pixinit/Infrastructure/Windows/` | Discovery, native interop and separate SATA/NVMe transports/providers |
| `pixinit/Infrastructure/Benchmarking/` | File benchmark, sustained partial engine and positively identified owned-file management |
| `pixinit/Infrastructure/History/` | SQLite history store |
| `pixinit/Infrastructure/Logging/` | Discovery/error logging |
| `pixinit/ViewModels/` | Shell, protocol, benchmark, history and shared observable/command state |
| `pixinit/Views/` | Separate protocol views, new BenchmarkView, metric cards and local WebOverview |
| `pixinit/Resources/` | Shared controls, diagnostic card styles and light theme |
| `pixinit/Assets/`, `pixinit/Properties/` | Icon and assembly metadata |
| `tests/pixinit.Tests/` | Behavior, parser, safety, history, specification, WPF, WebView2 and hardware harnesses |
| `build/`, `installer/` | Verification/release scripts and Inno Setup definition |
| `docs/` | Architecture, compatibility, phased reports, latest validation and screenshots; older reports describe older builds |
| `artifacts/` | Installer/hash, publish outputs, isolated builds and verification logs; generated content is not application source |
| `bin/`, `obj/`, `.git/` | Generated build intermediates and Git metadata, wherever present |
| Root documents | Project summaries, proposed design, supplied SSD PDF and READMEs |

## 4. Entry Points

- `pixinit/App.xaml` and `App.xaml.cs`: application startup and manual constructor composition. One StorageOperationGate is shared with discovery, SATA, NVMe and benchmark execution.
- `pixinit/MainWindow.xaml` and `.xaml.cs`: main shell, initial discovery after content rendering, close/cancellation coordination.
- `tests/pixinit.Tests/Program.cs`: executable harness entry.
- `build/verify.ps1`: Release build followed by harness checks; optional UI/hardware modes.
- `build/release.ps1`: verification, self-contained publish, asset checks, Inno compilation and SHA-256 generation; stops on failures.

## 5. Core Features: Done and Pending

| Feature | Current state | Remaining limitation |
| --- | --- | --- |
| Discovery | Implemented; capacity, full serial, firmware, bus, access, mount mapping and seek-penalty evidence | SSD/HDD classification is not established by this discovery path; UI says Not confirmed |
| SATA diagnostics | Separate read-only Identify/SMART provider, virtualized attribute table, raw bytes and discrepancies | Physical SATA validation and verified vendor attribute profiles pending |
| NVMe diagnostics | Separate controller/health/error provider, scope disclosure, consumed endurance and exact counters | Namespace Identify/mapping remains unavailable; per-namespace capability is not proof of namespace-scoped retrieval |
| Diagnostic UI | SATA/NVMe each have Overview, SMART, Details and Raw Data; compact identity/evidence cards | HTML Overview runtime verification remains incomplete |
| Workspace UI | Diagnostics, Benchmark and Diagnostic History tabs; unidentified devices remain inside Diagnostics | Broader accessibility/DPI verification pending |
| Sequential benchmark | Existing 1 MiB default blocks, byte/time throughput, separate MB/s and MiB/s | Buffered QD1 results, affected by caches |
| Random benchmark | Aligned random offsets, full-block reads, per-I/O latency including positioning; Quick default 1000 operations at 4 KiB | Standard/Custom counts remain configurable; not directly comparable to higher-QD device specifications |
| Sustained benchmark | New opt-in 1 GiB circular owned file, 1 MiB writes, up to 30 seconds or configured byte cap; interval samples and candidate-drop analysis | Full hardware run and confirmed post-SLC performance unverified |
| TBW presentation | NVMe host-write conversion; SATA conventional SMART 241 estimate; 600 TB reference allowance and warning rule | Manufacturer-rated TBW not verified; SATA units are unverified |
| Temperature | NVMe composite and conventional SATA 194 low-byte interpretation; 75-80 C yellow, >80 C red | Idle/load conditions are unknown; SATA encoding unverified; no added hardware sampling |
| History / exports | Diagnostic snapshots and complete BenchmarkSession JSON stored in SQLite; extended fields and samples round-trip | Existing history schema remains v2; no DB rewrite was needed |
| Installer | Latest unsigned self-contained x64 installer rebuilt | Latest install/upgrade/uninstall and installed launch not tested |

- Recent completed passes: full serial display; requested administrator manifest; installer finish-launch error 740 flag correction; workspace tabs; protocol secondary tabs; media-evidence disclosure; five SSD-specification cards/models/workloads.
- HTML Overview code is present with local data messages, activity indicator and percentage gauges. Its dedicated runtime tests have not recorded a successful completed run. Native WPF checks do not prove HTML support.
- Percentage Used is consumed endurance, never remaining health. TBW reference allowance is not device health.

## 6. App Flow

- Startup: compose providers/store/gate, show MainWindow, perform ordinary read-only discovery, populate protocol selections.
- Diagnostics: explicit read command, shared gate, coordinator/provider, allowlisted transport, parser/mapper, checklist assessment, ViewModel, snapshot/history/export.
- Benchmark: choose target/workload, validate policy, request exact write consent, re-resolve target and configuration/selection/free space, acquire gate, run engine away from UI thread, report progress, clean owned file, display/persist/export session.
- Progress phases: PREP, SEQ, RND and SUSTAINED. Sustained samples reach an ObservableCollection for the bounded ListView.
- Selection changes cancel/discard stale work and clear old benchmark readings. Shutdown waits for benchmark cancellation and cleanup.
- Attach diagnostic context to a benchmark only when its target reliably matches the selected drive. Retain observations as snapshots rather than fabricated load measurements.
- Historical readings retain stored timestamps, policy and methodology. Do not reinterpret them as current device observations.

## 7. State Management

- ObservableObject implements INotifyPropertyChanged; ActionCommand provides ICommand and explicit CanExecute refresh.
- ShellViewModel owns selections, discovery, separate SATA/NVMe states, benchmark and history ViewModels.
- StorageOperationGate is a global non-queuing lease for one storage worker in the composed app, not the proposed per-device SemaphoreSlim design.
- BenchmarkViewModel uses CancellationTokenSource, a generation counter, consent signatures, consent revalidation and a guard against late progress.
- Missing numeric measurements use nullable values/Availability; missing TBW hides its bar. Exact counters remain available in protocol details.

## 8. Storage / DB / API / IPC

- SQLite schema v2; default DB: `%LOCALAPPDATA%/PIXINIT/SataUtility/data/history.db`.
- SqliteHistoryStore uses transactions and JSON session payloads. New sustained samples, TBW, thermal context and random-rate properties persist without new columns.
- Benchmark files/manifests are created under a dedicated PIXINIT-owned directory in the chosen target. Cleanup verifies ownership and file identity; it must not delete arbitrary directories/files.
- WebView2 browser data: `%LOCALAPPDATA%/PIXINIT/WebView2`.
- WebOverview receives JSON presentation snapshots through WebView2 messages; no incoming storage command bridge or host objects. Local HTML is embedded as `pixinit.Overview.html`; external navigation is blocked in the wrapper.
- No external API or database credentials. Windows native device queries remain read-only; benchmark writes are filesystem writes, not raw sectors or ATA/NVMe write commands.

## 9. Important Functions / Classes

| Class / function | Responsibility |
| --- | --- |
| App.OnStartup | Manual composition, shared gate and main window |
| WindowsDiskDiscovery / DescriptorParser | Bounded Windows metadata parsing; retained seek evidence without guessed media classification |
| DiscoveryCoordinator / DeviceSelection | Discovery lifetime and protocol/device selection |
| SataOperationCoordinator / NvmeOperationCoordinator | Cancellation, operation lifetime and provider orchestration |
| WindowsAtaTransport / WindowsNvmeTransport | Separate native protocol transports and command safeguards |
| SataResultMapper | SMART status/identity plus conventional 194 temperature, 241 host-write estimate and 9 hours; labels unverified units |
| NvmeResultMapper | Health/controller/error mapping, host writes, temperature, power hours, discrepancies and scope |
| Core AssessmentPolicies | Existing protocol checklist assessment |
| Application HealthAssessor | Strict temperature thresholds and reference-endurance warning below 10 percent |
| TbwInfo / ThermalInfo | Nullable host writes, unverified 600 TB reference, allowance, observed temperature, unknown idle/load conditions |
| BenchmarkConfiguration / BenchmarkPolicy | Workloads, signatures, footprint, precomputed application-write budgets, free-space validation; policy v4 |
| BenchmarkSession / SpeedSample | Existing session model extended with random rates, sustained results/samples/drop status and diagnostic context |
| FileBenchmarkEngine.RunAsync | Preparation, ordered operations, partial results, cancellation and cleanup |
| SustainedWriteEngine.cs | Partial FileBenchmarkEngine implementation; bounded circular writes, sampling and AnalyzeSustained |
| AnalyzeSustained | Three baseline intervals plus three consecutive intervals below 70 percent of baseline; byte/time-weighted post-drop mean; no proven SLC attribution |
| OwnedBenchmarkFileManager | Owned-file creation, identity validation, orphan discovery and safe cleanup |
| BenchmarkTargetResolver / WpfWriteConsent | Volume/device mapping and explicit temporary-write approval |
| BenchmarkViewModel / BenchmarkView | UI configuration, worker execution, samples, five cards, history and export |
| SqliteHistoryStore / SnapshotFactory | Transactional persistence and source-aware snapshots |
| BenchmarkReportExporter / DiagnosticReportExporter | JSON/text reports; full serial or optional redaction |
| WebOverview / OverviewSnapshot | Local HTML presentation with native fallback; runtime validation pending |

## 10. Config and Environment Details

- App/version: PIXINIT 1.0.0; Windows x64 deployment. NuGet sources are constrained by `NuGet.Config` package-source mapping.
- Manifest currently requests `requireAdministrator`, per the earlier explicit user request. It declares PerMonitorV2 awareness; declarations do not establish tested DPI support.
- Installer uses Program Files, administrator setup, shortcuts/uninstall and `runascurrentuser` for finish-page launch. Writable data stays in LocalAppData.
- Ordinary workloads: 16-1024 MiB file; up to 3 GiB application data writes; QD1; existing iteration/random-operation bounds.
- Sustained workload: disabled by default; 1 GiB circular-file footprint; configurable 1-30 seconds; separate 1 MiB-32 GiB application-write cap, default 32 GiB. Reaching the cap may end before 30 seconds. A blocked native call can exceed the scheduling deadline.
- Free-space reserve: ordinary workloads use at least 3 GiB or 10 percent of available space; sustained-only uses at least 1 GiB or 10 percent, in addition to the required file footprint.
- Random `RndReadIOPS` / `RndWriteIOPS`: 1000 / mean I/O latency in ms. `RndReadMBs` / `RndWriteMBs` are MiB/s estimates = IOPS x 4096 / 1048576, despite the requested field names. Original operation Iops uses total measured wall time including inter-I/O overhead and final flush.
- Sustained `mbs` / SustainedWriteMBs use decimal MB/s. Actual interval durations and bytes are retained. A missing candidate drop yields unavailable post-drop speed.
- Measured writes use FlushAsync to the OS, not durable Flush(true). Read-file preparation still performs its existing durable flush outside primary throughput timing.
- Benchmark report schema: `pixinit.benchmark-report/1.1`. Comparisons require compatible target, identity, filesystem, configuration and policy; do not compare different policy versions automatically.
- No secrets or required external environment variables. Do not add NuGet dependencies without user approval.

## 11. Current Issues, Missing Parts and Verification Status

| Area | Latest evidence / pending work |
| --- | --- |
| Build | Release build passed with 0 warnings and 0 errors |
| Full applicable suite | 288 unique checks passed: 269 behavior checks, including 22 specification checks and nine volume tests, plus 19 WPF UI checks |
| Sustained safety | Disposable tests use only 1 MiB per sustained run; cover cap, cancellation, consent and cleanup. No full-duration physical benchmark was run |
| Screenshots | Actual WPF empty-state client renders at 1366 x 768 and 1800 x 1000; desktop benchmark inspected. Not installed-app or live-hardware validation |
| WebView2 | Dedicated --web-ui verification pending; teardown emits Chrome_WidgetWin_0 unregister error 1412 even when the WPF suite passes |
| Vendor semantics | SATA 194/241/9 interpretations are conventional estimates, not verified vendor profiles |
| Endurance | 600 TB is an unverified reference; actual manufacturer-rated TBW and confirmed NAND writes remain unavailable |
| Sustained performance | Observed speed drop heuristic implemented; proven SLC exhaustion/post-cache device speed unverified |
| Temperature | Known idle/load and post-benchmark observations unavailable without explicit measurement; no automatic diagnostic reads added |
| Hardware compatibility | Prior reports cover one Intel NVMe machine; physical SATA, Windows 11, other controllers/filesystems and broad compatibility remain unverified |
| Accessibility | 125/150 percent scaling, mixed DPI and screen-reader checks remain unverified; screenshots alone are insufficient |
| Packaging | Latest installer built successfully and unsigned; installation/upgrade/uninstall/launch not exercised for this build |
| Repository hygiene | No tracked GitHub Actions workflow, LICENSE or CONTRIBUTING found. Installer EXE is tracked; no tracked DLLs were found in the checked Git inventory |
| Documentation | Historical phase reports reflect older scope/budgets/hashes. Current root and project README files contain no embedded NUL bytes in the latest check |

- Installer: `artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe`.
- SHA-256: `F85A50900677A86049C4B167E90E015EA849B1B4360B1FFC961918977E9A45D3`; authoritative recorded file: `artifacts/installer/SHA256SUMS.txt`.
- Latest report and changed-file inventory: `docs/ssd-specifications-validation.md`.
- Recent supporting reports: `docs/media-evidence-ui-validation.md`, `docs/diagnostics-ui-validation.md`, `docs/workspace-tabs-validation.md`, `docs/full-data-permissions-validation.md`.
- Existing app can lock the normal Release executable. Use isolated build outputs rather than terminating the user's app.
- Reproducible latest verification commands, from repository root:

```powershell
dotnet build pixinit.slnx -c Release --artifacts-path artifacts/diagnostics-build
dotnet artifacts/diagnostics-build/bin/pixinit.Tests/release/pixinit.Tests.dll --ui
```

- Logs: `artifacts/verification/specs-build.log`, `specs-tests.log`, `specs-publish.log`, `specs-installer.log`.
- `--web-ui` runs a separate browser harness. `--hardware` runs hardware checks; neither is covered by the latest --ui total. Hardware benchmarks or additional diagnostic reads require applicable user authorization.
- Release status remains PARTIAL; passing fixture checks does not establish production readiness or support for all PCs.

## 12. What Is Needed for Good Architecture

- Preserve MVVM, manual constructor composition, current provider separation and the shared gate. A DI container is optional, not a prerequisite for correctness.
- Prioritize pending WebView2 lifecycle/runtime verification and real installer validation before claiming the HTML experience is verified.
- Add verified vendor/model SMART interpretation and endurance-rating profiles; retain unknown fields and raw evidence when profiles are absent.
- Design explicit, consented hardware validation for sustained behavior and known idle/load observations. Keep hard byte budgets and truthful cache limitations.
- Keep measurement units, provenance, policy versions and unavailable states explicit in UI, persistence and reports.
- Add a Windows CI runner for the existing executable harness; conversion to another test framework is optional and would require dependency approval.
- Consider benchmark/history interfaces where they enable meaningful test isolation; avoid broad rewrites of working transport code.
- Define repository artifact retention and licensing. Preserve historical evidence and user data; do not purge Git history or remove tracked artifacts without an intentional maintenance task.
- Keep documentation synchronized with actual code. Do not treat older 'sustained not implemented' reports as current scope, or proposed design classes as completed work.

## Next-Agent Constraints

- English only; no em-dash in new or edited documentation/code.
- No new NuGet packages without asking. Latest implementation uses the existing C#/WPF/SQLite/WebView2 stack.
- Preserve manifest and WebView2 setup unless the new task explicitly authorizes changes there.
- Never run a full physical write benchmark just to validate UI. Use bounded disposable tests; real workload consent is separate.
- Never claim buffered QD1 measurements establish uncached device speed, durable-write latency, physical NAND writes or proven SLC-cache exhaustion.
- Keep full serial display and optional export redaction. Do not mask values or fabricate missing data.
- Current updates are local; GitHub publication is not established by build or installer creation.

## Current Completion Pass - 2026-10-07

- Read this summary and SSD specification validation first. SYSTEM_DESIGN_SDE2.md remains a proposal; manual composition and shared StorageOperationGate are the implemented architecture.
- Diagnostics retain separate SATA/NVMe four-section layouts with a bounded, virtualized left device list and main content on the right. Full serial remains visible.
- BenchmarkView now owns the simple target/file-size/workload/duration controls, progress, cancellation, five performance cards, plain sustained sample ItemsControl, TBW reference bar, thermal status dot, historical readings and exports.
- Simple UI defaults: 1 MiB sequential blocks, 4 KiB random blocks, 1000 operations, one iteration. Other programmatic/Standard configurations retain their existing counts and are recorded accurately.
- File sizes are selectable from 16 through 1024 MiB. The latest ordinary application write cap is 3 GiB including read preparation. The default combined 1024 MiB read/write/random workload passes.
- Current policy is benchmark-policy-v4; report schema remains 1.1. Older policy runs cannot automatically compare with current methodology.
- Temperature display classification: Green below 75 C, Yellow 75 through 80 C, Red above 80 C. Evidence assessment remains separate.
- Missing thermal context and reference allowance explicitly show Unavailable. Idle/load readings are not inferred from ordinary observations.
- Cleanup opens the candidate without following the final reparse point, locks it against replacement, verifies volume/file identity and arms Windows handle deletion. The manifest is locked during validation; only empty owned directories are removed nonrecursively. Crash recovery still verifies orphan identities before deletion.
- Local HTML removes animation, shadows and conic gradients in favor of plain percentage bars. WebView2 setup and the native fallback are preserved.
- Final applicable harness: 279 unique passing checks, not the sum of baseline and final runs. Bounded sustained tests use only disposable 1 MiB budgets. No full physical write benchmark was run.
- Evidence logs: artifacts/verification/complete-baseline-build.log, complete-baseline-tests.log, complete-final-build.log, complete-final-tests.log, complete-publish.log and complete-installer.log.
- WPF screenshots are actual client renderings from the unavailable-discovery fixture at 96 DPI, including 1366x768 and 1800x1000. They do not establish real hardware or HTML runtime correctness.
- Pending: physical SATA/controllers/filesystems, full sustained hardware performance and SLC behavior, verified manufacturer endurance, known idle/load conditions, HTML runtime lifecycle, installer install/uninstall, 125/150 percent scaling, mixed DPI and screen-reader checks.
- Protected manifest, WebOverview.cs setup, project package references and NuGet.Config hashes match this pass's baseline.

- Current completion installer: artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe; SHA-256: F85A50900677A86049C4B167E90E015EA849B1B4360B1FFC961918977E9A45D3. Self-contained win-x64, unsigned. Published and compiled after the final 279-check suite; installer install/launch/uninstall NOT RUN. Tracked installer retained; no Git history deletion or distribution.

## Latest Storage Overview Pass - 2026-10-07

- Default Overview is shared native FirstPageView: Storage Usage followed by a compact sixteen-field SSD information grid. Previous WebOverview remains in collapsed additional diagnostic details; setup unchanged.
- Shared DeviceStorageViewModel partial exposes volume/capacity/usage properties inherited by SATA and NVMe. Uses mapped drive-letter roots and asynchronous DriveInfo with generation protection. It never measures a mounted folder's host root as that mounted volume.
- Multiple mapped drive letters use a ComboBox. Usage is selected filesystem capacity; physical device capacity remains a separate field. Missing/inaccessible volume capacity shows a capacity-only message with unknown used/free.
- Total, Used and Free are displayed in GiB and decimal GB. Free is AvailableFreeSpace, not necessarily total unallocated bytes under quotas. Used is explicitly total minus user-available free.
- All identity/telemetry fields retain unavailable and unverified states. Idle/load are not invented; unavailable host writes/temperature offer existing read-only diagnostic commands. Full serial and discrepancies remain visible.
- Other Devices tab removed; unidentified inventory retained in the Diagnostics expander. Unknown-only discovery opens it.
- No new packages, transport changes or benchmark writes. Corrected an existing BenchmarkPolicy brace syntax error while preserving write limits and free-space validation.
- Final Release build: zero warnings/errors. All 288 unique checks pass: 269 behavior checks and 19 WPF checks. Nine new tests verify volume mapping selection, counters, units, failures and stale results.
- Evidence: docs/storage-overview-validation.md and artifacts/verification/storage-overview-{build,tests,publish,installer}.log. Real-drive usage/mount correctness, quotas/spanned volumes, installer installation and non-100-percent DPI remain unverified.

- Latest storage-overview installer: artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe; SHA-256 F85A50900677A86049C4B167E90E015EA849B1B4360B1FFC961918977E9A45D3. Unsigned/self-contained win-x64; installation and uninstall NOT RUN.

## Latest Ordinary Benchmark Cap Fix - 2026-10-07

- BenchmarkPolicy.MaxOrdinaryApplicationDataWritesBytes is now 3,221,225,472 bytes (3 GiB); MaximumWriteBytes is a compatibility alias. Read preparation and ordinary timed sequential/random writes are all included.
- Quick workload with a 1024 MiB file, Include writes and Include Random requires 2,151,579,648 application data bytes and now passes policy validation. Large validation tests calculate budgets without executing large writes.
- Ordinary free-space reserve is max(3 GiB, 10 percent available space), in addition to required file footprint. Sustained-only workloads retain max(1 GiB, 10 percent); combined ordinary/sustained workloads use the ordinary reserve.
- Sustained cap remains separate at 32 GiB maximum with a 1 GiB circular file, opt-in consent and bounded cancellation/cleanup.
- Policy version is benchmark-policy-v4 so history comparisons retain the changed policy boundary. Report schema and workload signatures remain unchanged. Detailed requested/allowed-byte errors and corrected Validate braces are preserved.
- Benchmark UI now describes the 3 GiB cap and separate sustained limits. Existing tests are updated to cover accepting the requested largest Quick workload, rejecting workloads over 3 GiB and both reserve policies without increasing check count.

- Ordinary-cap final verification: Release build 0 warnings/errors; all 288 unique checks passed. Logs: artifacts/verification/ordinary-cap-build.log and ordinary-cap-tests.log. No 1024 MiB or other full physical write benchmark was executed; acceptance checks only validate configuration arithmetic.

- Ordinary-cap installer rebuilt after the passing 288-check suite: artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe; SHA-256 F85A50900677A86049C4B167E90E015EA849B1B4360B1FFC961918977E9A45D3. Unsigned; installation/uninstall NOT RUN.

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

## Current implementation override - company workflow
- This section supersedes earlier historical progress descriptions: two main tabs Diagnostics and Benchmark; Export History opens existing history UI; unidentified inventory is inside Diagnostics.
- Current policy is benchmark-policy-v3, ordinary cap/reserve 3 GiB, separate sustained cap 32 GiB and circular footprint 1 GiB. SafetyPolicy fingerprint prevents comparisons with earlier v3 payloads lacking the current safeguards.
- DeviceThermalViewModel provides timestamped current/idle/load context, cancellable ten-minute OS activity verification for idle, manual fresh load reads and automatic post-sustained reads through the shared gate. WindowsIdleMonitor uses built-in PDH; no additional dependencies.
- SatisfactionReportExporter creates company TXT/JSON reports with missing-value reasons and independent temperature/endurance/buffered sequential-read acceptance criteria. Company PASS is not a health assessment or universal capability guarantee.
- FirstPageView includes sixteen identity/telemetry fields plus shared usage and thermal capture cards. OtherDeviceViewModel supports unknown-device identity/usage without fabricated protocol telemetry.
- Final Release: 0 warnings/errors; 304 unique checks (285 behavioral, 19 WPF). Physical write validation was restricted to disposable 1 MiB tests.
- Pending: real idle/load captures, isolated installer lifecycle, physical SATA/USB/RAID, other controllers/filesystems, Windows 11, 125/150 percent/mixed DPI, screen reader and live WebView2 lifecycle. No proven SLC exhaustion or uncached/durable NAND measurements.
- Authoritative current evidence and installer hash: [docs/company-workflow-validation.md](docs/company-workflow-validation.md). Preserve existing manual composition, manifest, WebView2 setup, gate, owned-file safeguards, full visible serial and export redaction.
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
