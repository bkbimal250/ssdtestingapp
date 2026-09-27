# Phase 4 — read-only NVMe diagnostics

Validated 2026-09-27. Phase 4 implementation is complete. Phase 5 implementation readiness: **READY**. Hardware-release readiness remains **PARTIAL**: the available INTEL NVMe/Windows stack passed, while broader NVMe controllers, Windows 11, DPI configurations and physical SATA remain pending. Phase 5 was not started.

## Baseline

No applicable AGENTS.md or Git metadata exists in the project or its ancestors. The Phase 3 architecture, SATA transport/parsers, shared operation gate, discovery identity model, UI and validation reports were reviewed. The baseline Release build passed with zero warnings/errors, and both existing UI and hardware suites passed 114 checks. `phase-4-baseline-output.txt` preserves that run. The Phase 3 SATA implementation and its screen remain separate and operational.

## Transport and allowlist

WindowsNvmeTransport uses only `IOCTL_STORAGE_QUERY_PROPERTY` with `ProtocolTypeNvme`. It exposes an internal enum, never arbitrary admin command bytes or generic protocol-command passthrough. The fixed allowlist is:

| Operation | Property scope | Data type / request value | Payload bound |
|---|---|---|---:|
| Identify Controller | Adapter/controller (property 49) | Identify / CNS 01h | 4096 bytes |
| Identify Namespace | Device/namespace (property 50) | Identify / CNS 00h, verified NSID required | 4096 bytes |
| SMART/Health | Device (property 50) | Log Page / 02h | 512 bytes |
| Error Information | Adapter/controller (property 49) | Log Page / 01h | 512 bytes, eight 64-byte entries |

The request buffer is 8 bytes of STORAGE_PROPERTY_QUERY, 40 bytes of STORAGE_PROTOCOL_SPECIFIC_DATA, then a bounded payload. The latter includes ProtocolDataRequestSubValue2–4, all zero here. ProtocolDataOffset is 40 relative to ProtocolSpecificData. Format, sanitize, firmware changes, namespace management, Set Features, self-tests, writes, benchmarks and vendor mutation commands are unreachable through this API.

Only a confirmed direct NVMe discovery record (protocol NVMe, bus NVMe, native bus 17, interface path present) is accepted. Before every query, the implementation re-reads interface metadata, opens a shared zero-desired-access SafeFileHandle, and revalidates on that same handle. Path, known disk number, model, Windows-reported serial, firmware and capacity must agree with the selected record. SATA/unknown devices are rejected before dispatch. This guards reuse of a cached disk number and kept the actual ATA dispatch counter at zero.

Each output requires descriptor Version=48 and Size=48, ProtocolTypeNvme, the requested data type, an offset of at least 40, the exact requested data length, and checked bounds within both returned and allocated bytes. Allocation is at most 4144 bytes. A successful DeviceIoControl with an invalid descriptor is InvalidResponse. Native errors preserve AccessDenied, Unsupported, Disconnected and unexplained Failed outcomes. The fixed return DWORD, source, request scope, timestamp, parsed payload and bounded raw descriptor/response are retained. A failed optional Error Information query does not erase controller or health results.

The property query has no application-set timeout. It is synchronous; cancellation is checked before and after each call but cannot interrupt an in-flight DeviceIoControl. Handles and buffers remain alive until return, and the shared operation gate remains held. The app does not elevate or retry permission failures.

## Scope and parsing

Identify Controller is controller-wide. It parses plain ASCII model/serial/firmware (no ATA byte swapping), VID/SSVID, controller ID, reported NVMe version, namespace count, Log Page Attributes, zero-based ELPE capacity, warning/critical composite temperature thresholds and supported host-controlled thermal-management indication. Namespace count is not a physical-drive count.

Identify Namespace support and its pure parser are implemented, including NSZE/NCAP/NUSE validity, active LBA format, metadata size, logical block size and BigInteger byte capacities. The query requires a nonzero, independently established NSID. Phase 2 discovery does not expose one, and disk number is not an NSID, so production correctly records Namespace as **NotQueried**. Controller and health results remain usable. Namespace size/capacity/utilization are not fabricated, and discovery capacity remains visibly sourced from Windows geometry.

SMART/Health uses the Windows device query. Identify Controller LPA bit 0 determines the displayed scope: selected device/namespace when per-namespace SMART is reported, otherwise controller-wide. The numeric NSID remains unavailable. Parsed fields include Critical Warning and its five defined bits plus unknown bits, composite temperature, spare and threshold, Percentage Used, ten unsigned 128-bit counters, warning/critical temperature time, eight temperature sensors and four thermal-management counters.

The 128-bit counters use BigInteger. Data Units convert by `value × 1,000 × 512` bytes, with decimal TB and binary TiB shown separately. The UI says that the source value is rounded up and that zero Data Units means not reported. These cumulative values are not MB/s, IOPS, rated TBW or NAND writes. Other legitimate zero counters remain visible. Percentage Used is estimated endurance consumed, may exceed 100, and is never converted to remaining health. Kelvin zero/FFFF sensors are unavailable; supported values convert using K − 273.15. Zero thermal-management counts mean never occurred or not implemented. Time fields retain minutes, hours or seconds as defined.

Critical Warning 00h is described as “No critical warning reported,” followed by the individual indicators and a statement that this is not an overall health/QC result. Unknown warning bits remain hexadecimal. Error Information is controller-wide; the request is capped at eight entries and only issued when ELPE establishes at least eight. Zero ErrorCount slots are omitted, populated fields remain raw, and the lifetime health-log counter is kept separate from the number retrieved. Neither a positive count nor an empty bounded sample is promoted into a current failure verdict.

Discovery versus Identify model, firmware and serial comparisons are shown without overwriting either source. Raw details contain the controller serial and are labeled accordingly. PCIe generation/lane width remains unavailable; it is not inferred from NVMe version or model.

## UI and lifecycle

The existing NVMe screen now has an explicit Read NVMe Diagnostics action and progress states for controller identity, SMART/Health, bounded error information and result preparation. Discovery still sends no diagnostic queries. The screen displays Critical Warning indicators, composite temperature, spare and threshold, estimated endurance consumed, large usage counters with exact values and conversions, sensor/thermal data, controller/namespace evidence, errors and a scrollable technician raw-response expander. Details include source, scope and observation time. The raw TextBox supports keyboard selection/copy. The SATA screen and SATA models were not reused.

Discovery, SATA and NVMe coordinators share StorageOperationGate. The shell maintains separate SATA/NVMe generations and cancellation sources. Selection changes, rescans, cancellation and close discard obsolete/partial results; blocked native work keeps the gate until it returns. Switching tabs does not alter or misattach results. Optional per-query failures remain visible beside successful partial results.

Laptop (1264×661 DIPs) and desktop (1784×961 DIPs) production captures are `screenshots/phase4-nvme-laptop.png` and `screenshots/phase4-nvme-desktop.png`. They were visually inspected: the explicit action, metric cards and bounded outer scrolling remain readable; long counters use full-width detail sections rather than shrinking their text.

## Verification

Final `build/verify.ps1 -Ui -Hardware`: **PASS**. Release build: **0 warnings, 0 errors**. Complete output is `phase-4-verification-output.txt`.

| Suite | Shared behavior checks | Additional checks | Suite total |
|---|---:|---:|---:|
| Behavior only | 146 | 0 | 146 |
| WPF UI | 146 | 8 | 154 |
| Actual hardware/runtime | 146 | 14 | 160 |

**168 unique checks = 146 behavior + 8 UI + 14 hardware.** Suite totals overlap and must not be added. All 106 pre-Phase-4 behavior checks remain; 40 new behavior checks cover controller/namespace/health/error parsing, wide arithmetic, descriptor validation, exact request policy, outcomes, partial failure, identity rejection and lifecycle. Fixtures are named SYNTHETIC and are not captured drive responses. Production has no fixture fallback.

## Actual INTEL NVMe results

Non-elevated Windows 10.0.19045 x64; device `INTEL SSDPEKNW512G8`, firmware `004C`; serial redacted. Windows discovery reported 512,110,190,592 bytes and automatically selected NVMe. Two explicit reads were performed without polling:

- Identify Controller: **Success**, 4096 bytes, controller-wide adapter query. Model and firmware matched discovery. Identify reported NVMe 1.3.0, VID/SSVID 8086/8086, controller ID 1, one namespace, LPA 0Fh, 256 Error Information slots, warning threshold 350 K and critical threshold 353 K. Windows and Identify serial representations differed; both sources remain distinct and the discrepancy is visible. Same-handle Windows identity revalidation still passed before each query.
- Identify Namespace: **NotQueried** because no numeric NSID was established. No value was inferred from Disk 0.
- SMART/Health 02h: **Success**, 512 bytes, selected device/namespace scope because LPA reports per-namespace SMART support. Critical Warning 00h; composite temperature 38.85 °C during the final run; Available Spare 100%; threshold 10%; Percentage Used 5%.
- Usage: Data Units Read 39,210,130 (approximately 20.076 TB / 18.259 TiB); Data Units Written 30,719,514 (approximately 15.728 TB / 14.305 TiB); host reads 637,278,361; host writes 658,373,296; busy time 18,626 minutes; 1,320 power cycles; 8,283 power-on hours; 411 unsafe shutdowns; zero media/data-integrity errors; lifetime Error Information count zero. Values are a time-specific observation and can advance between reads.
- Additional sensors: unavailable. Warning/critical temperature time was zero minutes. HCTM transition/time counters were zero, meaning never occurred or not implemented.
- Error Information 01h: **Success**, 512 bytes, controller-wide. Eight bounded slots were examined; zero populated entries. This agrees with the lifetime count at that moment but does not prove future/current overall health.
- Second read: the health query remained available and selection/tab were preserved. Six total native NVMe queries were dispatched (three per read). ATA dispatches stayed **zero**.

The production report with timestamps and per-query outcomes is `phase-4-hardware-observations.txt`. No equivalent independent Windows source exposed these health metrics during the run, so no fabricated metric comparison is claimed. Model, firmware and physical capacity were cross-checked against the independent Phase 2 Windows discovery path.

## References and remaining validation

- [Microsoft: Working with NVMe drives](https://learn.microsoft.com/en-us/windows/win32/fileio/working-with-nvme-devices) — property-query scopes, request structure, Identify/Get Log Page examples and descriptor validation.
- [Microsoft: STORAGE_PROTOCOL_SPECIFIC_DATA](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ns-winioctl-storage_protocol_specific_data) — 40-byte fields, relative data offset and request values.
- [Microsoft: NVME_IDENTIFY_CONTROLLER_DATA](https://learn.microsoft.com/en-us/windows/win32/api/nvme/ns-nvme-nvme_identify_controller_data) and [NVME_IDENTIFY_NAMESPACE_DATA](https://learn.microsoft.com/en-us/windows/win32/api/nvme/ns-nvme-nvme_identify_namespace_data) — implemented Identify fields.
- [Microsoft: NVME_HEALTH_INFO_LOG](https://learn.microsoft.com/en-us/windows/win32/api/nvme/ns-nvme-nvme_health_info_log) and [NVME_ERROR_INFO_LOG](https://learn.microsoft.com/en-us/windows/win32/api/nvme/ns-nvme-nvme_error_info_log) — payload layouts and error entry validity.
- [NVM Express Base Specification 2.0d](https://nvmexpress.org/wp-content/uploads/NVM-Express-Base-Specification-2.0d-2024.01.11-Ratified.pdf), SMART/Health and Identify definitions — counter units, scopes, reserved/unavailable meanings and thermal fields.

Significant new files: Core/Diagnostics/Nvme/NvmeParsers.cs and NvmeResultMapper.cs; Infrastructure/Windows/Nvme/WindowsNvmeTransport.cs and WindowsNvmeDiagnosticsProvider.cs; Application/Scanning/NvmeOperationCoordinator.cs; tests/pixinit.Tests/NvmeTests.cs. Existing NVMe result/ViewModel/view, shell, contracts, App composition and hardware harness were integrated.

Before a release claim, repeat these exact read-only steps on Windows 11 and representative in-box/vendor NVMe controller stacks, including multi-namespace devices where a trustworthy NSID mapping is available: run Release verification non-elevated, explicitly read twice, record each query/scope, compare identity/capacity to Windows discovery, inspect zero/reserved fields, then exercise selection/cancel/rescan/close. Record AccessDenied/Unsupported rather than elevating automatically. Verify laptop/desktop and pending high/mixed-DPI layouts. Physical SATA validation remains NOT RUN. No BIOS/storage-mode changes or drive removal are required.
