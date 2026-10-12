# Benchmark UI and Accuracy Validation - 2026-10-10

## Changes
- Quick, Deep and Custom modes; collapsed Advanced settings; segmented 512/1024/2048 MiB sizes. Unbuffered/write-through sequential/random reads and writes remain enabled in the visible workflow. Legacy buffered history remains readable.
- Defaults: 1024 MiB, three passes, five seconds per profile, 15 seconds sustained with a separate 16 GiB ceiling. Deep uses 30 seconds with the same sustained ceiling. These are phase durations; the complete session takes substantially longer than 15 seconds.
- Ordinary timed writes retain the 96 GiB ceiling. With sustained enabled, the requested total can approach 112 GiB. The consent dialog retains the exact total application-byte allowance. No NAND-write or daily-wear saving is promised.
- Suggested target uses Desktop/PIXINIT_TEST only when Desktop belongs to the selected mapped volume; otherwise it uses PIXINIT_TEST on that volume. The folder is created on Start after mapping/free-space validation. Changing folders on C: does not remove OS background activity.
- Free space is queried asynchronously. More than 10 GiB is required before consent and rechecked before writes. Existing allocation/reserve checks remain. Observed target-volume fullness above 80 percent reduces sustained duration to five seconds. No particular resulting speed or SLC condition is inferred.
- Fixed cancellation during asynchronous preflight. Start/settings are disabled during preflight; cancellation prevents preparation and owned-file writes. Existing operation gate, mapping revalidation, consent and cleanup remain.
- Responsive result cards show average, best and completed-pass counts; random cards retain IOPS. All-profile cards preserve distinct queue profiles, with each original pass in tooltips. Averages are not combined across incompatible queue profiles.
- One progress bar shows percentage, elapsed time, estimated remaining time and reported application writes. Intermediate speed/pending-average and consent-status lines are removed from the visible page. Invalid-attribution warnings remain visible.
- TBW uses the existing verified manufacturer rating where available, a written-budget bar and remaining-budget badge. Unverified references remain labeled. Endurance budget remaining is not remaining health.
- Current temperature is prominent; idle/load capture is optional and compact. Unknown idle/load conditions are not fabricated. Overall sustained and optional post-drop throughput remain separate.
- Footer and discovery status follow diagnostic snapshot presence. Valid observed zero writes remains queried. Device changes clear the snapshot/footer. Historical readings do not populate the live footer.
- Windows PnP current-link lookup reads the first NVMe-class PCI ancestor. Unknown values and other PCI controller classes retain unavailable status. Link observations/source persist in device snapshots and performance notices retain the observed link instead of claiming it is unavailable. No throughput-based generation/lane estimate or NVMe Identify link inference is used.

## Actual accuracy findings
- Read-only discovery returned INTEL SSDPEKNW512G8, PCIe 3.0 x4, from Windows PnP properties. Log: artifacts/verification/benchmark-revamp-discovery.log. No health-log reads or normal physical benchmark were run for this validation.
- No new evidence establishes Intel model-specific speed limits, healthy/exhausted SLC cache, or CrystalDiskMark equivalence. The badge describes measured results without a fabricated within-spec or health guarantee.
- Property definitions: [Microsoft SDK pciprop.h](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/shared/pciprop.h). Retrieval: [CM_Get_DevNode_PropertyW](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_get_devnode_propertyw).

## Final verification
- Command: ./build/release.ps1
- Release build and self-contained win-x64 publish: passed, 0 warnings/errors.
- All 352 unique executable checks passed: 325 behavior checks and 27 WPF checks. Focused reruns overlap this total.
- Existing tests use disposable bounded fixtures. Native/sustained failure validation uses 1 MiB caps; no full physical benchmark was run.
- Actual running WPF client captures at 1366x768, 1800x1000 and 760x640 use empty or explicitly synthetic fixtures. Visually inspected captures are UI evidence, not physical performance evidence:
  - docs/screenshots/phase7-responsive-1366x768.png
  - docs/screenshots/phase7-responsive-desktop.png
  - docs/screenshots/benchmark-cards-1366x768.png
  - docs/screenshots/benchmark-cards-1800x1000.png
  - docs/screenshots/benchmark-minimum.png
- Logs: artifacts/verification/benchmark-revamp-build.log, benchmark-revamp-ui.log, benchmark-revamp-release.log and benchmark-revamp-discovery.log.
- Host DPI: 96. 125/150 percent scaling, mixed DPI and screen reader: NOT RUN. Manual check: select Settings > System > Display > Scale, restart PIXINIT at 125 and 150 percent, review tabs, card wrapping, scrollbars and keyboard focus without starting a benchmark.
- Physical SATA/additional controllers and Windows 11 qualification remain unverified. No new NuGet; manifest, WebView2 setup and manual composition preserved.

## Installer
- Path: artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe
- SHA-256: 9DE718C6A643A419A193332FF2BB279173E30585960EF028FAA38A1CA6FD0071
- Inno Setup compilation passed. Package is unsigned. Installation, upgrade, launch and uninstall of this exact package: NOT RUN in isolation. No publishing/distribution performed.

## Changed source
- pixinit/Core/Devices/StorageDevice.cs, pixinit/Core/History/DiagnosticSnapshot.cs and pixinit/Core/Benchmark/DirectBenchmarkModels.cs
- pixinit/Infrastructure/Windows/Discovery/WindowsDiskDiscovery.cs and PcieLinkReader.cs
- pixinit/ViewModels/Shared/DeviceViewModel.cs and pixinit/ViewModels/Shell/ShellViewModel.cs
- pixinit/ViewModels/Benchmark/BenchmarkViewModel.cs, DirectBenchmarkViewModel.cs and BenchmarkPresentationViewModel.cs
- pixinit/Views/Benchmark/BenchmarkView.xaml and .xaml.cs; pixinit/Views/Nvme/NvmeView.xaml; pixinit/MainWindow.xaml
- tests/pixinit.Tests/Program.cs, BenchmarkRevampTests.cs, BenchmarkContextTests.cs and UnbufferedBenchmarkTests.cs
- Summary, README, compatibility, this report, screenshots, installer and checksum
