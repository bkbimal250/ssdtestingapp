# Phase 5 — assessment, history and report export

Validated 2026-09-27. Phase 5 is complete. Phase 6 benchmark scope has not started. Release readiness remains **PARTIAL** because physical SATA, broader controllers, Windows 11, accessibility and high/mixed DPI remain unverified.

## Baseline and scope

The pre-change Release baseline passed 154 UI/behavior checks and 160 checks with the actual-hardware path. The existing Phase 1–4 discovery, SATA/NVMe transports, typed outcomes, nullable metrics, lifecycle guards and two protocol tabs were retained. No benchmark, write workload, raw-drive write, new transport, vendor profile, installer or namespace guess was added.

Phase 5 adds `Microsoft.Data.Sqlite` 10.0.12, a protocol-specific assessment layer, immutable snapshots, a history panel below the protocol tabs, conservative comparison, and redacted TXT/JSON export. SQLite and export are ordinary application-file writes only.

## Assessment contract and checklist

Query outcome, device-reported status and application assessment remain separate. An assessment records severity, coverage, checklist result, checks, missing evidence, source, UTC observation time, protocol scope, stable rule ID and rule-set version. Current policies are `pixinit.sata.health/1.0` and `pixinit.nvme.health/1.0`.

`No Issues Detected` means the evaluated evidence showed no issue. It does not predict future reliability. Coverage is `Complete`, `Partial` or `Insufficient` independently of severity. PASS requires every mandatory check to be valid and passing. A known failure produces FAIL even with partial coverage. Missing mandatory evidence prevents PASS and produces INCONCLUSIVE when no definite failure exists. The checklist explicitly excludes performance, full-surface integrity and future reliability.

| Protocol / input | Condition | Result | Rationale |
|---|---|---|---|
| SATA SMART RETURN STATUS | Valid device response says threshold exceeded | Critical / FAIL | Device-reported threshold condition; separate from future reliability |
| SATA SMART data and thresholds | Either validated structure is missing | Coverage incomplete; no PASS | Missing input never silently passes |
| SATA normalized attribute | Unique ID match and current/threshold are valid; current ≤ threshold | Warning / FAIL | Uses only the defined normalized comparison |
| SATA raw attribute | No verified vendor/model profile | Excluded / not applicable | No temperature, wear, sectors, TBW or media-failure meaning is invented |
| NVMe Critical Warning bit 0 or 1 | Spare or temperature warning set | Warning / FAIL | Bits are interpreted individually |
| NVMe Critical Warning bit 2, 3 or 4 | Reliability degraded, read-only, or volatile-memory backup warning set | Critical / FAIL | Definite serious device-reported condition remains visible with partial coverage |
| Unknown Critical Warning bits | Any reserved bit set | Attention | Value is preserved without assigning a meaning |
| Available Spare | Valid spare is below the device-reported threshold | Warning / FAIL | Uses the reported threshold, not a manufacturer guess |
| Composite temperature | Valid value reaches the controller-reported warning/critical threshold | Warning/Critical / FAIL | No application-defined temperature threshold is used |
| Media/Data Integrity Errors | Cumulative count is positive | Attention | Historical count alone does not prove a current failure |
| Percentage Used | Value is at least 100% | Attention | Presented as estimated endurance consumed; never converted to remaining health |
| Unsafe Shutdowns / lifetime Error Information count | Any value | Informational | Nonzero historical counters do not become unexplained failures |

SATA communication/error raw fields are not interpreted, so they cannot be promoted to media failure. SSD/HDD-specific SATA meanings require a verified profile; none is present. Manufacturer-rated TBW and NAND writes display `Unavailable`. Host reads/writes remain NVMe device counters with exact decimal values and approximate decimal/binary byte conversions, never throughput or IOPS. Main temperature is rounded; exact Celsius and Kelvin-derived controller thresholds remain in details and snapshots.

The rules follow the ATA SMART structural references already recorded in Phase 3 and the NVMe SMART/Health definitions recorded in Phase 4: [Microsoft NVME_HEALTH_INFO_LOG](https://learn.microsoft.com/en-us/windows/win32/api/nvme/ns-nvme-nvme_health_info_log), [Microsoft NVME_IDENTIFY_CONTROLLER_DATA](https://learn.microsoft.com/en-us/windows/win32/api/nvme/ns-nvme-nvme_identify_controller_data), [NVM Express Base Specification 2.0d](https://nvmexpress.org/wp-content/uploads/NVM-Express-Base-Specification-2.0d-2024.01.11-Ratified.pdf), and the [T13 ACS-3 working draft](https://read.seas.harvard.edu/cs161/2020/pdf/ata-atapi-8.pdf). These references define fields and status meanings; they do not supply a universal failure prediction.

## SQLite history, retention and identity

The database is `%LocalAppData%\PIXINIT\SataUtility\data\history.db`. Schema version 1 is created in a transaction and recorded in `PRAGMA user_version`. A newer unknown version fails without deleting or rewriting the database. Foreign keys are enabled on each connection.

- `sessions` stores immutable snapshot JSON plus identity, protocol/scope, completion, assessment, parser/rule version and UTC time.
- `query_outcomes` stores ordered outcome, scope, source, time, explanation, native error and raw BLOB under a cascading session foreign key.
- `metrics` stores value as nullable decimal text with units, availability, source, time and scope. BigInteger/UInt128 values never enter SQLite INTEGER or floating point.
- `settings` stores the visible retention period. The initial value is two days.

Snapshot insertion is one transaction. Forced mid-insert failure leaves no partial row. Completed, cancelled and failed attempts have explicit completion states; cancelled/failed attempts use Unknown/Insufficient/INCONCLUSIVE and are never called complete. Startup cleanup runs asynchronously, deletes only sessions strictly older than the cutoff, and cascades related application rows transactionally. Database initialization, save and cleanup failures leave live diagnostics usable, update the nonblocking history status, and write the technical cause to the existing local log.

Automatic matching requires serial, PnP instance ID and interface path together; the comparison key is a SHA-256 digest including protocol/model/capacity evidence. Disk number, model or duplicate serial alone is insufficient. Uncertain identities remain separate and disable automatic comparison. A comparison also requires the same protocol, scope, units, source and increasing timestamp. Decreasing counters are labeled reset/incompatible and receive no manufactured positive delta. History is paged 25 rows at a time and can filter by protocol or the selected reliable device.

## Export schema

The history export actions also target the most recently completed live snapshot until a historical session is selected. TXT and JSON include report schema `pixinit.report/1.0`, observation/export timestamps, identity, protocol/scope, query outcomes, metrics/units/sources, checklist evidence, missing information, limitations, and parser/rule/schema versions. Serial redaction is on and raw payload inclusion is off by default. JSON stores all metric values, including large counters, as lossless decimal strings in `Snapshot.Metrics[].ValueText`; `LargeIntegerRepresentation`, `SerialRedacted` and `RawPayloadsIncluded` document these choices. A temporary file plus atomic replacement prevents partial final reports. Invalid destinations, cancellation and write failure do not discard the current result or stored snapshot.

## Actual INTEL NVMe validation

The production app ran non-elevated on Windows 10.0.19045 x64 against `INTEL SSDPEKNW512G8`, firmware `004C`; serial is redacted. Two explicit reads each dispatched three read-only NVMe property queries and zero ATA commands. Identify Controller, SMART/Health 02h and bounded Error Information 01h succeeded. Namespace remained `NotQueried` because no numeric NSID exists in discovery.

The final assessment was **No Issues Detected**, coverage **Complete**, checklist **PASS**. Evidence was Critical Warning 00h; 40.85 °C (41 °C rounded in the main UI) against controller-reported 76.85/79.85 °C thresholds; Available Spare 100% against 10%; Percentage Used 5%; Media/Data Integrity Errors 0; broader lifetime Error Information count 0. Unsafe Shutdowns was 411 and remained informational. Manufacturer-rated TBW, NAND writes, namespace values and PCIe link details remained unavailable.

Both sessions reopened through a new store instance. Fifteen same-device/same-scope metrics were comparable. The selected snapshot exported to `phase-5-intel-nvme.txt` and `phase-5-intel-nvme.json`; every JSON metric value matched the reopened snapshot, serial was redacted, raw payloads were omitted and the TXT retained the original rule version. Full evidence is in `phase-5-hardware-observations.txt`.

## Verification and remaining work

There are **32 unique isolated Phase 5 checks** for rules, coverage, checklist behavior, full-precision/lossless persistence, raw payloads, rollback, migration safety, retention boundary, history state/filtering/restart, identity/scope comparison, decreasing counters, export options and failure isolation. The actual-machine harness adds **8 unique Phase 5 checks** for real assessment evidence, namespace withholding, restart/reopen, comparison and export fidelity. The complete hardware run passed **200 checks**; the isolated UI run passed **186**. Release build passed with **0 warnings and 0 errors**.

Actual production screenshots were visually inspected at 1264×661 and 1784×961 DIPs: `screenshots/phase5-assessment-laptop.png` and `screenshots/phase5-history-desktop.png`. They show the existing NVMe tab with assessment and the expanded historical detail/filter/export area.

Before a release claim, repeat read-only validation on physical SATA, Windows 11, other in-box/vendor NVMe controllers, multi-namespace hardware with an independently established NSID, access-denied/unsupported stacks, keyboard/screen-reader flows, high/mixed DPI and multiple monitors. Validate retention and report save dialogs under restricted profile/file permissions. Benchmark scope planning may begin only after workload type, limits, write behavior and user consent are explicitly settled; no Phase 6 code exists.
