# Company workflow validation - 2026-10-07

## Current result
- Release readiness: PARTIAL. Universal Windows, controller and filesystem support is not established by these tests.
- Final Release build: 0 warnings, 0 errors. All 304 unique checks passed: 285 behavioral checks and 19 WPF checks. This includes the existing checks; do not add older overlapping totals.
- Commands: `dotnet build pixinit.slnx -c Release --artifacts-path artifacts/diagnostics-build` and `dotnet artifacts/diagnostics-build/bin/pixinit.Tests/release/pixinit.Tests.dll --ui`.
- Logs: `artifacts/verification/company-build.log`, `company-tests.log`, `company-publish.log`, `company-installer.log`.
- No full physical benchmark or real diagnostic capture was run. Write tests use disposable bounded 1 MiB configurations and injected telemetry.

## Implemented behavior
- Two main tabs: Diagnostics and Benchmark. History remains available through Export History. Unidentified devices have an inventory/usage expander, opened automatically for unknown-only discovery.
- FirstPageView remains the SATA/NVMe default Overview. Its sixteen fields include media and access evidence, full serial and current temperature. Idle/load capture has a separate card. Missing telemetry remains explicit with diagnostic/capture actions.
- Shared drive-letter usage is read off the UI thread with selection-generation checks. Physical capacity and filesystem totals remain distinct. Unmapped/inaccessible volumes retain capacity-only fallback, never invented zero usage.
- Folder-mounted benchmark targets are rejected rather than attributed to their host drive. USB/RAID devices can show discovered identity/usage; unconfirmed diagnostic passthrough is not assumed.
- Policy is benchmark-policy-v3 as requested. Ordinary application data writes have a 3 GiB cap/reserve; sustained uses its separate 32 GiB cap, 1 GiB circular file and sustained-only 1 GiB reserve. Reserves are also at least 10 percent of available free space. Mixed workloads retain the ordinary reserve.
- Validation accepts Quick, 1024 MiB, writes, random and sustained 30 seconds with 5,000,000,000 available bytes. This is configuration validation, not a physical run or a promise that an inaccessible volume works. Larger iteration budgets still reject excessive writes.
- A persisted SafetyPolicy fingerprint prevents comparison with historical v3 sessions lacking these safety rules. Report schema stays 1.1.
- Benchmark diagnostics are linked through the existing shared StorageOperationGate. The benchmark lease is released before a post-sustained diagnostic read. Cancellation, device generation, shutdown and selected-device identity checks remain active.
- Sustained overall average uses actual total bytes/time in decimal MB/s. Optional post-drop speed is separate. No qualifying drop means overall remains available; a candidate does not prove SLC exhaustion.
- Idle capture requires the previous benchmark to be older than ten minutes and then observes ten minutes of zero reported disk transfers and queue activity. Missing/invalid counters, activity or cancellation refuse an idle claim. It performs a fresh diagnostic read afterward.
- Automatic load capture waits ten seconds after a completed sustained run and starts a fresh diagnostic read only within sixty seconds. Manual capture reads immediately. Both record observation time and conditions; neither proves peak load temperature.
- Thermal display thresholds remain Green below 75 C, Yellow 75 through 80 C, Red above 80 C. Evidence-based health assessment remains separate.
- Company TXT/JSON export includes identity, filesystem/mapping evidence, metrics, missing-value reasons and three independent criteria: verified temperature below 80 C, consumed endurance below 90 percent, buffered sequential read above 500 MB/s. Missing criteria produce INCOMPLETE, not PASS. The 600 TB reference is not a manufacturer rating.
- Normal TXT/JSON history exports retain thermal capture timestamps/conditions and safety policy. Full serial is displayed; export redaction remains available.
- Manifest, WebView2 setup, project dependencies and NuGet configuration match the recorded protected hashes. No new NuGet packages. Existing AlgoTwist shell branding was preserved.

## Changed areas
- Core: BenchmarkPolicy, BenchmarkModels, Common/StorageUsage and new ThermalInfo.
- Application: BenchmarkTargetResolver, BenchmarkReportExporter and new SatisfactionReportExporter.
- Infrastructure: SustainedWriteEngine and new Windows/Monitoring/WindowsIdleMonitor.
- View models: shared DeviceViewModel/storage/thermal/other-device presentation, SATA/NVMe, ShellViewModel and BenchmarkViewModel.
- Views: MainWindow, FirstPageView, BenchmarkView, new ThermalCaptureView, shared card styles and local Overview.html corner styling.
- Tests: new CompanyWorkflowTests and affected existing navigation/storage-specification assertions.

## Actual UI evidence
- Actual running WPF client render captures, injected empty/unavailable fixtures at 96 DPI; these are not photos of a physical drive or proof of DPI support.
- Laptop 1366 x 768: `docs/screenshots/phase7-responsive-1366x768.png` and `post-phase7-nvme-1366x768.png`.
- Desktop 1800 x 1000: `docs/screenshots/phase7-responsive-desktop.png` and `post-phase7-nvme-desktop.png`.
- SATA empty state: `docs/screenshots/sata-desktop.png` and `sata-minimum.png`. No fake SATA readings were inserted.
- Checked scrollable layouts, two-tab navigation, unavailable states, long identity containment and history popup assertions through the WPF harness. The captures retain the injected discovery-not-connected fixture message; they do not describe production discovery.

## Outstanding verification
| Configuration/check | Status |
| --- | --- |
| Release build and bounded executable/WPF harness | PASS |
| Windows x64 self-contained publish and unsigned installer compilation | PASS; see installer hash below |
| Installer install, launch, upgrade and uninstall in isolation | NOT RUN |
| Actual ten-minute PDH idle capture and automatic physical post-load diagnostics | NOT RUN; injected orchestration tested |
| Physical SATA, USB/RAID passthrough, other controllers and filesystems | NOT RUN |
| Windows 11 and broad Windows 10 compatibility | NOT RUN |
| 125/150 percent, mixed DPI and screen reader | NOT RUN |
| WebView2 live lifecycle and runtime availability on another machine | NOT RUN |
| Uncached speed, durable NAND latency, proven SLC exhaustion | Not claimed or measured |

- DPI manual check: Settings > System > Display > Scale, choose 125 percent then 150 percent, relaunch, verify both tabs at laptop/desktop sizes, keyboard focus and all scrollable actions. Repeat when moving between monitors. Do not run a write benchmark for UI verification.
- PDH counters are OS observations, not additional ATA/NVMe commands. API references: [PdhGetFormattedCounterArrayW](https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhgetformattedcounterarrayw), [PdhAddEnglishCounter](https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhaddenglishcountera). Counter availability on other machines remains unverified.
## Installer
- Path: `artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe`.
- SHA-256: `550C8FBFFC1EC9C617400AD54226F24E9FB653183793B49EAC78CB4FBCDEAD10`.
- Windows x64 self-contained; signature status: NotSigned. No trust certificate generated. Install/launch/upgrade/uninstall: NOT RUN. Not published or distributed.
