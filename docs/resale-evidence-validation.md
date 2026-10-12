# Historical report: proof export retired

- The following describes the previous build. PDF, evidence JSON, QR generation and all user-facing export actions were removed in the bulk no-export pass. See bulk-no-export-validation.md for current behavior.

# Bulk inventory and resale evidence validation

## Implemented

- Retained SetupAPI and Win32_DiskDrive independent inventory, serial/capacity/physical-number deduplication and native volume extent mapping. MSFT_PhysicalDisk is now consulted every discovery; FriendlyName retained as fallback evidence. Added Win32_DiskPartition inventory cross-check logging. Partitions do not become duplicate physical disks and do not override actual volume mappings.
- Fixed actual Windows COM failures when reading WMI uint64 properties/enumerating records: explicit Properties_.Item(...).Value and balanced ReleaseComObject instead of final-releasing shared wrappers. Live host now reports all four sources successfully.
- Windows/model display fallback extends to USB alongside RAID. Reported/inferred SSD media and heuristic protocol labels remain separate from verified transport permissions. Virtual disks remain visible without being represented as physical SSDs.
- Export Proof button captures all inventory entries, retained per-device diagnostic snapshots and the current saved benchmark session without running hardware commands. Previous snapshots after rescans explicitly historical. Missing diagnostics/health are unavailable, not zero or PASS. Full serials included deliberately.
- C# PDF exporter adds UTC export/observation times, PC name/OS, identity, capacity in GB/GiB, protocol/bus/media/access/evidence, checklist coverage, exact observed metrics, query scope, measurements and limitations. SATA snapshot now also retains temperature and mapped power-on hours. Existing storage/thermal/benchmark UI remains.
- Output is PDF plus evidence JSON and PDF .sha256 sidecar. PDF prints SHA-256 of exact evidence JSON bytes. Valid QR version 1-L encodes PX plus the first fourteen hash hex characters. Full SHA-256 remains on the report. QR is an offline identifier, not a hosted verification page or authenticity seal. Hashes are unkeyed; they cannot establish authorship or prevent coordinated rewriting.
- No EVM authenticity certification, reseller authorization claim, invented health percent or universal speed PASS/FAIL. Report states these are not verified. Benchmark Completed is workflow completion, not certification. Full measured per-pass evidence is preserved without new expected-performance thresholds.
- Export uses an immutable captured evidence object and background file generation. Existing manual composition, operation gate, cancellation, manifest, write consent/caps and owned cleanup retained. PDF/QR implementation adds no NuGet or browser dependency.

## Actual verification

- Release pipeline passed with 0 warnings/errors and 396 unique checks: 391 previous plus 5 PDF/hash/QR geometry regressions. No full physical write benchmark.
- Actual Windows 10 read-only discovery twice: SetupAPI one disk; Win32_DiskDrive one record; Win32_DiskPartition three records; MSFT_PhysicalDisk one record. Direct Intel NVMe and observed PCIe 3.0 x4 unchanged. No physical SMART reads during validation.
- PDF fixture explicitly TEST ONLY: parsed with pypdf, rendered with Poppler, visually inspected for spacing/wrapping, and QR independently decoded with ZXing and matched to the JSON hash. QA tools live only under verification artifacts, not product/publish. Poppler emitted local optional display-font warnings; the built-in Courier/Helvetica PDF rendered and parsed successfully.
- Evidence: artifacts/verification/resale-release.log, resale-build.log, resale-discovery.log, resale-pdf-qa.log. Test-only PDF/PNG: artifacts/verification/resale-proof/test-only-resale-proof.pdf and .png. These are not client resale proof.
- Publish contains only self-contained pure WPF .NET 8 PIXINIT.exe, 67,271,950 bytes (67.27 MB). Installer 62,328,751 bytes (62.33 MB); both below 100,000,000 bytes. WPF trimming remains disabled.
- Installer artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe; SHA-256 A0A569113DF9961FB1B37DF3F3254DD3DD22E1281B0C7B445EB60A64C6C5C957. Unsigned, not installed in this pass.

## Coverage limits and pending work

- NOT universal controller support. Standard direct SATA/NVMe and validated RAID probing retained; Intel RST/AMD RAIDXpert proprietary miniport, legacy SMART_RCV_DRIVE_DATA and USB SAT diagnostic transport are NOT IMPLEMENTED. No invented NVMe-over-SCSI CDB sent. A model-derived protocol never authorizes hardware commands.
- Actual Windows 11 three-disk client, USB SSD/SAT, RAID-controller variants, VHD and all-three physical benchmarks NOT RUN. No guarantee the client's three disks will all expose SMART. Unsupported coverage remains explicit in reports.
- Automated batch health reads, external trusted signatures/timestamps/QR hosting, manufacturer authenticity validation and documented reseller authorization are not implemented. Current workflow: scan inventory, read each verified device manually, optionally benchmark with explicit consent, then Export Proof.
- PC evidence currently includes machine name and OS, not verified OEM/BIOS authenticity. No future lifespan or guarantee inferred from health counters. No global SSD health percentage invented.
- Installer upgrade/uninstall, runtime-free isolated machine, 125/150 percent scaling, mixed DPI and screen-reader checks remain pending. Release readiness PARTIAL.

## Operator workflow

- Install latest build, Scan Drives, inspect reported versus heuristic badges and coverage.
- Read diagnostics for each verified target before export. Unsupported devices remain represented with unavailable metrics. Filesystem benchmarks require correct volume mapping and explicit write consent; benchmark performance is not authenticity proof.
- Export Proof, retain PDF, evidence JSON and SHA sidecar together. A customer can recompute SHA-256 locally; QR identifies the evidence hash prefix. An unsigned report can be replaced by anyone, so retain original evidence under controlled storage.
- Send %TEMP%/PIXINIT_detect.log plus controller/driver identity when RAID metrics remain unavailable. Do not change boot RAID BIOS mode as a diagnostic workaround.
