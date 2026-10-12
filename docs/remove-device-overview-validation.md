# Remove duplicate device overview and temperature capture

- Removed Device overview TabItem from MainWindow.xaml. No standalone DeviceOverviewTab/View/ViewModel existed. Diagnostics retains SATA/NVMe views and their Overview, SMART and Details sections; storage usage and full identity remain. All detected disks still appear in the sidebar/top selector. Unconfirmed protocols route to Additional Devices, hidden at zero, without guessing protocol or telemetry.
- Deleted ThermalCaptureView XAML/code-behind and WindowsIdleMonitor. Removed Capture Idle, Capture Load, Cancel capture commands, busy/cancellation state, idle verification and automatic post-sustained capture/delay. DeviceThermalViewModel now exposes only current provider thermal observations. FirstPageView no longer adds the duplicate Temperature observations card. Benchmark keeps current temperature/value/color/observation and diagnostic-context text without capture buttons.
- Shared operation gate, diagnostic reads, benchmark configuration/consent, native unbuffered flags, byte limits, cancellation and positively identified temporary cleanup retained. No benchmark results or example temperatures/speeds are hardcoded. No new packages, no exports, no SQLite/history calls. Legacy optional idle/load DTO fields remain for pure result/serializer compatibility; no workflow populates them through capture.
- Changed MainWindow, FirstPageView, BenchmarkView, DeviceViewModel/DeviceThermalViewModel, shell/benchmark VMs and corresponding UI/discovery/workflow tests. Retired obsolete capture tests. A bounded one MiB sustained fixture now checks completion with current temperature context and no additional capture.

## Verification

- Focused actual WPF harness: 33 checks passed, including two protocol tabs, absence of manual capture controls, laptop/desktop/minimum layout and virtualized SMART. Screenshot fixtures under docs/screenshots, not client hardware proof.
- Final Release build: zero warnings/errors. Full applicable suite: 392 unique checks, including the focused UI coverage. No full physical benchmark used for validation.
- Source audit: no Device overview tab, thermal-capture command/workflow or SQLite package/store in the app. Shared current temperature comes from SATA/NVMe provider results.
- Logs: artifacts/verification/remove-overview-build.log, remove-overview-ui.log, remove-overview-release.log.

## Limits

- Installer unsigned; install/upgrade/uninstall not tested. Windows 11 client RAID and physical mixed-drive verification unavailable. Vendor USB/SAT and proprietary RAID SMART coverage remains partial; this removal does not add controller support. DPI 125/150 percent not tested.
- Existing user DB files remain untouched and unused. Self-contained pure WPF .NET 8; trimming disabled because unsupported by WPF.

## Release artifacts

- Self-contained publish: PIXINIT.exe only, 66,046,626 bytes (66.05 MB).
- Installer: artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe, 61,109,845 bytes (61.11 MB), size check PASS below 100 MB.
- SHA-256: 60C319503836742575509471BADB0FC1037C99D51F834F79DBB22B9843A013DA.
- build/release.ps1 completed successfully and build/size-check.ps1 passed. No distribution or installation performed.
