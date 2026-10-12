# Bulk diagnostics without exports

## Implemented changes

- Removed ProofPdfExporter, ProofQr and ProofTests. Removed ExportProofCommand and snapshot cache dedicated to exporting. Removed history and benchmark file export commands and the history export controls. No application PDF, proof JSON or QR generation entry point remains. Existing internal history JSON payloads and legacy report serializers/tests remain for compatibility; they are not user export actions.
- SATA and NVMe diagnostics each retain distinct Overview, SMART and Details. Both sidebars now display the complete AllDevices inventory and bind selection to SelectedDisk, which routes to the correct protocol view. Cards show model, GB/GiB capacity, protocol/RAID heuristic badge, access/health availability and serial suffix. Full serial remains in the main diagnostics.
- Four-source SetupAPI/Win32_DiskDrive/MSFT_PhysicalDisk/Win32_DiskPartition discovery, mount mapping and dedup retained. Duplicates require matching serial, capacity and physical number; distinct physical disks sharing controller serials are preserved. Logs remain under TEMP/PIXINIT_detect.log.
- Added client-shaped three-disk tests: validated EVM SATA plus provisional SATA 256GB and NVMe 512GB, with duplicate interface removed. Windows Storage SSD reports and model hints can enable explicitly heuristic navigation, never native SMART authorization.
- Fixed USB fallback log wording to retain the actual reported bus. No manufacturer authenticity, health percentage or sample benchmark values are invented.
- Microsoft.Data.Sqlite 10.0.12 remains the only direct NuGet dependency. No QRCoder, ZXing, QuestPDF, PdfSharp, Newtonsoft.Json or browser package is used by the application. Admin manifest, consent, shared operation gate, bounds, cancellation and owned-file cleanup preserved.

## Verification

| Check | Actual result |
| --- | --- |
| Release build | 0 warnings, 0 errors |
| Final build/release.ps1 | PASS; 396 unique checks, including 32 WPF checks |
| Focused WPF run | 32 checks passed separately; overlaps final total |
| Actual discovery-only run | Windows 10, one Intel SSDPEKNW512G8, PCIe 3.0 x4 |
| Source logs | SetupAPI 1, Win32_DiskDrive 1, MSFT_PhysicalDisk 1, Win32_DiskPartition 3 partitions |
| Client-shaped RAID simulation | Three disks, two SATA/one NVMe; duplicate removed; two heuristics cannot issue native diagnostics |
| UI runtime | Actual WPF harness at laptop/desktop/minimum sizes, host 96 DPI; test identity data explicitly fixtures |
| Self-contained publish | PIXINIT.exe only, 67,261,279 bytes (67.26 decimal MB) |
| Inno installer | 62,317,172 bytes (62.32 decimal MB), unsigned, installation NOT RUN |

Commands: `build/release.ps1`; `build/size-check.ps1` runs within release; `dotnet artifacts/diagnostics-build/bin/pixinit.Tests/release/pixinit.Tests.dll --ui-only`; `dotnet artifacts/diagnostics-build/bin/pixinit.Tests/release/pixinit.Tests.dll --discovery-only`.

Logs: artifacts/verification/bulk-no-export-release.log, no-export-ui.log, bulk-no-export-discovery.log. Screenshots produced by the WPF harness are in docs/screenshots; they are not client RAID hardware proof. No full physical benchmark or new physical write test was run; existing bounded disposable harness tests ran.

Installer: artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe.

SHA-256: `8CB3335EC3C1B893957ADAC822690D5E4DE216B4BAA420E5518791FEBFA2152D`.

## Remaining coverage limits

- PARTIAL: Windows 11 client with Intel RST/AMD RAID and all three disks, USB SAT transport and physical SATA diagnostics were not tested here. Proprietary RAID miniport and USB SAT health transport are not implemented. Heuristic devices can appear in Diagnostics with Limited access; actual SMART is unavailable until identity-attributable transport is supported.
- No universal all-controller support, EVM authenticity certification, healthy PASS from missing telemetry, model-rated endurance from SMART, or fixed expected benchmark speed is claimed.
- Target E:/PIXINIT_TEST and the supplied 546.06/500.43 MB/s figures are client examples, not measurements produced by this validation. Target selection/mapping and user consent remain mandatory; no E: volume exists on this host.
- Batch diagnostic health scanning, independent installed runtime-free validation, installer install/uninstall and 125%/150% DPI are NOT RUN or unimplemented. Compression below 100 MB does not guarantee extracted runtime disk footprint below 100 MB. WPF trimming stays disabled.
- Automatic approval review rejected cleanup of historical verification PDF/QR QA artifacts (blocked by policy). They remain outside publish/installer. The old resale-evidence-validation.md is explicitly marked historical; application proof source and commands are removed.
