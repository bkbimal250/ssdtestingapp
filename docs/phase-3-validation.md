# Phase 3 — read-only SATA diagnostics

Validated 2026-09-26. Phase 3 implementation is complete. Phase 4 implementation readiness: **READY**. Physical SATA hardware validation: **NOT RUN**; SATA hardware-release readiness remains **UNVERIFIED**. No NVMe diagnostics, benchmarks, firmware operations, disk writes, vendor interpretation database or overall health/QC assessment were added.

## Baseline and shell evidence

The original Release build and 60-check UI suite passed. The baseline hardware suite reached successful discovery/rescan but failed its 1 ms dispatcher-timer assertion: fast discovery can finish before that timer fires. `phase-3-baseline-output.txt` preserves this failure. The check now explicitly queues dispatcher work across the asynchronous scan; it is a responsiveness smoke check, not a driver latency guarantee. One intermediate build encountered the running project executable locking its output; that window was closed normally and the build rerun successfully.

The preserved `baseline/MainWindow.phase2-before.xaml.txt` contains the empty starter Grid, not the tabbed shell. Restoring the working shell in Phase 2 was necessary for its tab/layout tests and usable application UI. There is no Git history or other evidence explaining how the empty version replaced the shell; no cause is asserted. Backup SHA-256: `52C1C45AD421B05339C9B8758AA7C90CC2E59FA3216C4B7FBD6698A60CBE839C`. It remains preserved. Active MainWindow has the SATA/NVMe tabs and discovery controls; App.xaml still loads Light.xaml and Controls.xaml. This phase changes the SATA view locally and App composition, without rewriting MainWindow or resource dictionaries.

## Command and transport boundary

Only a confirmed direct SATA record (protocol SATA, bus SATA, native bus 11, interface path present) is accepted. Metadata is re-read before opening the diagnostic handle, and again on the very handle used before every ATA command. Path, known disk number, model, known serial, firmware and capacity must match. Disk number alone is insufficient. Unknown identity evidence prevents dispatch. Missing serials limit the strength of identity matching; session identities are not persistent device identities.

| Operation | Command | Features | LBA mid/high | Sector count | Direction / payload |
|---|---|---|---|---|---|
| IDENTIFY DEVICE | EC | 00 | 00/00 | 00 | Data-in / 512 bytes |
| SMART READ DATA | B0 | D0 | 4F/C2 | 01 | Data-in / 512 bytes |
| SMART READ THRESHOLDS | B0 | D1 | 4F/C2 | 01 | Data-in / 512 bytes |
| SMART RETURN STATUS | B0 | DA | 4F/C2 | 00 | Non-data / 0 bytes |

Unused task-file bytes, previous task file and reserved fields are zero. Sector count is unused for these SMART commands; the fixed data request uses 1. Internal enum-based request generation and byte-for-byte request validation exclude arbitrary commands, SMART configuration/self-tests, SET FEATURES, security actions, sanitize/erase, firmware updates and sector writes. No command/register API is exposed to UI input.

Buffered IOCTL_ATA_PASS_THROUGH is 0x4D02C. Validated x64 header size is 48 bytes, pointer-sized DataBufferOffset is at byte 24, CurrentTaskFile at byte 40, payload at byte 48. Input is header-only; output is 48 or 560 bytes. Flags are DRDY_REQUIRED plus DATA_IN for payload reads, with no DATA_OUT, DMA or 48-bit command flag. Requested timeout is 10 seconds per command. Header size, flags, returned/allocated/transfer lengths and payload offset must match. BSY/DRQ, zero/all-FF status, DF and ERR are rejected; Windows success alone is insufficient. ABRT alone is not labeled unsupported. Returned payloads (bounded to 512 bytes), task-file registers, timestamps, outcome and native error are retained.

The IOCTL contract requires a GENERIC_READ | GENERIC_WRITE handle; this is an access requirement, not permission to issue write commands. Handles share read/write access. AccessDenied is reported without automatic elevation or retry. SafeFileHandle and managed buffers remain alive through synchronous calls. Cancellation cannot forcibly interrupt DeviceIoControl or metadata calls; the worker and shared operation gate remain held until they return. The driver timeout is a request, not an enforced wall-clock bound. No USB SAT, RAID, bridge-specific or vendor transport fallback exists.

## Parsing and displayed evidence

Pure parsers have no Windows handle dependency. IDENTIFY requires exactly 512 nonzero/non-FF bytes, rejects ATAPI/incomplete data, and validates the byte checksum when the A5 signature is present. Absence of the optional checksum is disclosed. Word-swapped model/serial/firmware, valid LBA28/LBA48 counts, valid logical/physical sector sizes, ATA version bits, SATA capability word, SMART support/enabled state, TRIM, NCQ/depth, rotation, form factor and security flags are decoded. Reserved/invalid fields remain unavailable; no unconditional 512-byte sector assumption is used. Extended sector-count formats are explicitly withheld. Discovery and ATA identity/capacity remain separate; discrepancies are displayed, and a differing ATA serial prevents further SMART commands.

SMART data and threshold sectors require exact length, checksum and a nonzero/non-FFFF revision. The legacy 30 × 12-byte record layout is decoded; ACS treats these bytes as vendor-specific, so this is structural decoding, not verified vendor semantics. Every nonzero ID is retained, including unknown IDs and duplicates. Missing IDs with nonzero bytes produce warnings; all-FF records are flagged. Thresholds match by unique ID, never position; duplicate IDs/thresholds or reserved FF thresholds withhold matching. Each attribute retains flags, current, worst, raw six bytes, slot and an explicitly labeled UInt48 little-endian display convention. These numbers have no assumed units or percentages.

SMART RETURN STATUS recognizes only valid completion registers with mid/high 4F/C2 (threshold not exceeded) or F4/2C (threshold exceeded). Unrecognized responses are InvalidResponse/Unknown. Neither result is overall Healthy or QC PASS. Temperature, wear, endurance, Windows TRIM configuration and negotiated link speed remain unavailable. Maximum capability is not current link speed. No vendor/model interpretation was added; F1/F2 are not assumed to mean LBAs and HDD attributes receive no SSD wear rules.

## Integration and lifecycle

App injects a dedicated SATA coordinator/provider and a shared discovery/diagnostic operation gate. Startup and Scan Drives only discover disks. Read SATA Diagnostics explicitly refreshes the selected direct SATA drive, displaying opening/identity/attributes/thresholds/status/preparing stages. The existing table remains virtualized; selected-attribute details include flags/raw encoding/interpretation limits. Identity/features, SMART-specific status, discrepancies and a technician raw-data expander are populated. Raw IDENTIFY bytes can contain the complete serial and the UI says so.

Selection changes, rescans and close invalidate diagnostic generations. Cancellation discards partial results; late progress/results cannot populate another selection. The UI prevents rescans during diagnostics, and the coordinator gate also rejects conflicting programmatic operations. Cancellation does not unlock a still-running worker. Access denied, disabled SMART, unsupported requests, invalid responses, disconnection and unexplained failures remain distinct. No automatic SMART enable is attempted.

## Verification results

Release build: **PASS, 0 warnings, 0 errors**. `build/verify.ps1 -Ui -Hardware` passed; complete output is in `phase-3-verification-output.txt`.

| Suite | Shared behavior checks | Additional checks | Suite total |
|---|---:|---:|---:|
| Behavior only | 106 | 0 | 106 |
| WPF UI | 106 | 8 | 114 |
| Actual hardware/runtime | 106 | 8 | 114 |

**122 unique checks = 106 behavior + 8 UI + 8 hardware.** The two full suite totals overlap; 228 executions are not 228 unique checks. All 52 previous behavior checks remain; 54 new checks cover pure parsers, allowlist/native boundaries, provider behavior and lifecycle. Fixtures are explicitly SYNTHETIC and test-only; they are not captured drive responses. No production mock fallback exists.

Coverage includes word-swapped identity, LBA28/LBA48/4K sectors, invalid/reserved fields, short/zero/FF/checksum-invalid data, malformed lengths/offsets, ATA errors despite IOCTL success, unknown/malformed/duplicate attributes, reordered/missing/duplicate thresholds, recognized/unrecognized status, disabled SMART, denial/unsupported/disconnection, disk-number reuse, non-SATA rejection, cancellation/close/late results and shared operation locking. WPF checks cover bounded layout, table virtualization and separate protocol views. Captures were visually inspected; isolated UI screenshots use a test shell without discovery and are not hardware evidence.

Actual machine: Windows 10.0.19045 x64, non-elevated token, one INTEL SSDPEKNW512G8 NVMe (bus 17), firmware 004C, 512110190592 bytes. Automatic discovery selected NVMe; the SATA screen stayed empty with its read action disabled. Native ATA dispatch count remained **zero** through startup and rescans. `phase-3-hardware-observations.txt` records the run. `screenshots/phase3-laptop.png`, `phase3-desktop.png` and `phase3-sata-empty.png` are actual production-App runtime captures. Physical SATA per-command results: **NOT RUN — no SATA drive available**.

## Authoritative references

- [Microsoft ATA_PASS_THROUGH_EX](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddscsi/ns-ntddscsi-_ata_pass_through_ex): field widths, flags, task-file indexes and buffer offset contract.
- [Microsoft IOCTL_ATA_PASS_THROUGH](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddscsi/ni-ntddscsi-ioctl_ata_pass_through): buffered input/output, synchronous operation and read/write access requirements.
- [T13/2161-D revision 5, ACS-3 working draft](https://read.seas.harvard.edu/cs161/2020/pdf/ata-atapi-8.pdf): section 7.12.7/table 45 and SMART commands. IDENTIFY words 0, 10–19, 23–26, 27–46, 49, 60–61, 69, 75–76, 80, 82–83, 85–87, 100–103, 106, 117–118, 128, 168–169, 217 and 255 underpin the implemented fields. This is a named working draft, not a claim of certification against a purchased final standard.
- [Smartmontools primary ATA definitions](https://www.smartmontools.org/static/doxygen/atacmds_8h_source.html): legacy SFF-8035i-style SMART attribute/threshold structures and commands. Used for the legacy structural convention, not vendor semantic mappings.
- [Hitachi Travelstar 7K500 OEM specification, hosted by Western Digital](https://documents.westerndigital.com/content/dam/doc-library/en_us/assets/public/western-digital/product/internal-drives/eol/travelstar-series/product-manual-travelstar-7k500-standard-models.pdf): section 14.40 documents the legacy SMART family. Its model-specific semantics are not generalized to other devices.

## Significant files and later validation

New production files: Core/Diagnostics/Sata/{AtaModels,AtaIdentifyParser,SmartParsers,SataResultMapper}.cs; Infrastructure/Windows/Sata/{AtaCommandPolicy,WindowsAtaTransport,WindowsSataDiagnosticsProvider}.cs; Application/Scanning/{StorageOperationGate,SataOperationCoordinator}.cs. Existing SATA contract/models/ViewModel/view, ShellViewModel, DiscoveryCoordinator and App composition were integrated. Tests add SataTests.cs and hardware safety observations. Architecture, compatibility, README and roadmap reflect this phase.

On a Windows x64 machine with an already-connected internal SATA drive:

1. Build Release and run verification. Record OS, controller/driver, drive model/firmware and permission mode. No system-drive removal or BIOS/storage-mode change is needed.
2. Start normally, confirm discovery classifies the intended drive as direct SATA, then explicitly click Read SATA Diagnostics. Record all four command outcomes, returned status/error registers, timestamps and any access denial. Do not enable SMART if disabled.
3. Compare displayed identity, byte capacity and sector sizes to trustworthy device documentation/metadata. Review raw bytes locally; redact serials before sharing evidence. Unknown vendor readings must remain uninterpreted.
4. Refresh, change selection, cancel, close and rescan after completion. Confirm old values never appear under another drive. Do not unplug an internal/system disk for this test. Use an already-safe removable test arrangement only for later removal testing.
5. Repeat with representative SATA SSD/HDD and controller/permission combinations before claiming hardware support. If access is denied, record it; an explicitly chosen administrator run may compare permissions, but the app never elevates itself.

Remaining limits: no physical SATA evidence, no USB/RAID transport, no vendor-derived temperature/wear/endurance, no NVMe diagnostics, no overall health, unverified other OS/architectures and high-DPI/controller matrix. Phase 4 may begin as a separate implementation task; Phase 3 SATA release support still requires this hardware matrix. This task stops at Phase 3.
