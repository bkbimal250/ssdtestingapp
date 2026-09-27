# Architecture

The baseline was a single WPF starter project, with an empty MainWindow and no providers, tests, dependencies, scripts or application logic. No applicable AGENTS.md was found in the project or ancestor directories. The directory has no Git metadata; there was no tracked diff to inspect. Existing .vs, generated files and project user settings were left intact.

Keep one production assembly until separate deployment or compile-time isolation justifies more projects. The folder boundaries below are logical dependencies, not compiler-enforced assembly boundaries. Core has no WPF or Windows API references in its source.

```text
pixinit.slnx
pixinit/
  App.xaml(.cs)                    Composition root and shared resources
  MainWindow.xaml(.cs)             Shell; initial work-area sizing
  Core/
    Devices/                      Identity, protocol, bus, capability, access
    Diagnostics/Common/           Nullable metrics and operation state
    Diagnostics/Sata/             ATA SMART result and attributes
    Diagnostics/Nvme/             Native NVMe health result
    Assessment/                   Versioned SATA/NVMe evidence rules and checklist contract
    History/                      Immutable snapshot and conservative comparison contract
    Benchmark/                    Versioned workload, result, limit and comparison contracts
    Abstractions/                 Discovery and separate provider contracts
  Application/
    Discovery/                    Cancellation-aware background coordination
    Selection/                    UI-independent selection policy
    Reporting/                    TXT/JSON snapshot export with redaction
    Benchmarking/                 Filesystem target mapping and write-consent contract
  Infrastructure/Windows/Discovery/
                                  SetupAPI discovery, bounded descriptor parsing and volume mapping
  Infrastructure/Windows/Interop/ Native API declarations and SafeHandle ownership
  Infrastructure/Logging/        Local discovery errors with Win32 codes
  Infrastructure/History/        Versioned transactional SQLite store
  Infrastructure/Benchmarking/   Bounded owned-file engine and manifest cleanup
  ViewModels/{Shell,Sata,Nvme,History,Benchmark,Shared}/
  Views/{Sata,Nvme,Shared}/
  Resources/{Themes,Styles}/
tests/pixinit.Tests/               Executable behavior and WPF verification
build/verify.ps1                  Repeatable release build/checks
docs/                             Architecture, roadmap, validation, compatibility
  screenshots/                    Running WPF client captures
```

Dependency direction: Desktop ViewModels → Application → Core. Infrastructure → Core contracts. App startup composes WindowsDiskDiscovery and Application, then injects ShellViewModel. Views bind to ViewModels. View code-behind handles layout and the initial visible-window lifecycle event. Native calls remain exclusively in Infrastructure; Core and Application have no native implementation dependencies.

MainWindow stays at the original path to preserve project familiarity. There is no artificial `src` relocation. Vendor-specific interpretation remains absent until verified evidence exists. Phase 5 adds conservative protocol rules rather than a health score engine: raw SATA attributes stay uninterpreted, NVMe uses standardized fields and controller-reported thresholds, and missing mandatory evidence blocks PASS.

## Selection policy

Devices are ordered by stable identity using ordinal comparison. Each protocol remembers its own selected ID. On first nonempty discovery, prefer the system physical disk only when exactly one result is explicitly marked unambiguous and its underlying protocol is known. Otherwise select SATA first, then NVMe, and use the first ordinal ID within the protocol. Unknown devices remain outside both lists.

Rescans retain connected selections. A disconnected selection falls back within its protocol. An explicitly user-selected tab is retained even when empty, including later rescans. Otherwise the active tab may move to the other populated protocol. A revision captured at scan start protects tab/device choices made while discovery is pending. The shell suppresses transient selection changes caused by rebinding device lists. Every completed discovery or device switch clears diagnostics, and results for a different device ID are rejected.

Discovery runs through a single guarded background worker in the existing coordinator. Production injects WindowsDiskDiscovery. One scan starts after ContentRendered; manual rescans use the same pipeline. Failure/cancellation retains and marks previous inventory stale. Successful enumeration, including a genuine empty result, replaces it. Scan generations and cancellation checks reject obsolete results. The 30-second budget requests cancellation, but synchronous native calls cannot be forcibly interrupted; new scans remain disabled until the worker actually exits. Closing the window cancels acceptance without waiting on the dispatcher. Native buffers/handles remain owned by the worker until each call returns. Discovery does not invoke diagnostics; SATA and NVMe diagnostics use separate explicit actions.

## Accuracy

Metrics carry nullable values, units, availability, source, observation time and optional interpretation. Available readings require value/source/time; nonavailable readings cannot carry values. Unsupported, access denied, error and unavailable are distinct from a valid zero. SATA SMART pass/fail and verified wear remain separate from NVMe Critical Warning, Available Spare and Percentage Used. Percentage Used is never converted to remaining health. Manufacturer specification ingestion is absent; future specifications must be labeled separately from live readings.

Test identities and readings exist only in the test executable. Production has no fixture fallback. Discovery, SATA and NVMe diagnostic refreshes each use their own generations.

## Phase 2 discovery policy

SetupAPI enumerates present disk interfaces rather than guessed PhysicalDrive numbers. STORAGE_DEVICE_DESCRIPTOR provides model, serial, firmware, removable flag and raw bus. Only BusTypeSata (11) and BusTypeNvme (17) establish protocol. ATA, USB, SCSI, SAS, RAID, Spaces and virtual buses remain Unknown. Seek-penalty metadata is reported as evidence but never converted to an SSD/HDD assertion. Media therefore remains Unknown with the current conservative backend. No CIM/WMI or speculative pass-through fallback is used.

Session identity is SHA-256 of normalized PnP instance plus interface path; absent instance ID falls back to the interface path. No serial/model/disk-number merging occurs. Identical keys are deduplicated; conflicting bus evidence withholds classification. These keys are not persistent hardware identities and may change or be reused across reconnects. All diagnostic values clear on rescans, so uncertain identity matching cannot carry health data forward.

Windows volume is resolved using GetWindowsDirectoryW → GetVolumePathNameW → GetVolumeNameForVolumeMountPointW. Volume disk extents can contain several extents on one disk; preference requires exactly one distinct disk number, exactly one matching enumerated device, and a known direct protocol. Multi-disk/virtual/denied/unavailable mapping produces no preference and a visible limitation. Volume enumeration adds mount points; these are not capacity values. Physical capacity is the DiskSize returned by disk geometry, shown in decimal GB with exact bytes in details. Volume sizes/free space are not queried.

SafeHandle owns device sets, file handles and volume enumerators. Interface detail allocation uses finally disposal; IOCTL arrays stay alive throughout synchronous calls. Descriptor headers, lengths, string offsets, terminators, extent counts and allocations (1 MiB cap) are validated. Optional query failures preserve partial records. Enumeration failure before a complete interface list preserves the entire previous snapshot. Logs at `%LOCALAPPDATA%\PIXINIT\Logs\discovery.log` include query errors and Win32 codes, excluding serials and raw paths.

## Phase 3 SATA boundary

App composes WindowsSataDiagnosticsProvider through SataOperationCoordinator and shares StorageOperationGate with DiscoveryCoordinator. The gate is held until the background worker returns, including after cancellation. ShellViewModel snapshots selection/generation, rejects late results and clears old values on selection and discovery. Read SATA Diagnostics is explicit; startup discovery never dispatches ATA.

Core/Diagnostics/Sata contains independent IDENTIFY, SMART data, threshold and status parsers plus result mapping. Infrastructure/Windows/Sata owns the internal four-command allowlist, bounded buffered pass-through and same-handle identity revalidation. WindowsSataDiagnosticsProvider sequences these reads; it does not enable SMART or infer vendor semantics. The UI receives typed outcomes, source-specific identity, raw records and unknown interpretation. No arbitrary register input crosses the application boundary. Read/write handle access is required by the IOCTL contract, while the allowlist permits only the four read-only operations.

Application/Scanning holds the coordinators/gate; native calls remain in Infrastructure. SATA ViewModel/view add stages, explicit refresh, SMART-specific status and a bounded raw expander while preserving virtualization. See phase-3-validation.md for command details, field references, cancellation limits, tests and hardware qualification gaps.

## Phase 4 NVMe boundary

WindowsNvmeDiagnosticsProvider runs through NvmeOperationCoordinator and the same StorageOperationGate. ShellViewModel keeps an independent NVMe generation/cancellation source, clears readings on discovery or selection, and rejects cancelled, late or wrong-device results. Startup and Scan Drives never issue protocol queries.

Infrastructure/Windows/Nvme exposes only four typed IOCTL_STORAGE_QUERY_PROPERTY requests: controller Identify, verified-NSID namespace Identify, device SMART/Health log 02h and controller Error Information log 01h. It performs metadata and same-handle identity checks before every query. Descriptor version, size, protocol/data types, relative offsets, returned length, requested length and allocation bounds are checked before parsing. No arbitrary admin-command or IOCTL_STORAGE_PROTOCOL_COMMAND surface exists.

Core/Diagnostics/Nvme keeps independent controller, namespace, health and error parsers/models. It uses BigInteger for 128-bit counters and wide namespace capacities, preserves raw responses and source/scope/time, and maps each query outcome separately. Namespace parsing exists but production withholds the request without an established numeric NSID. The UI labels selected-device versus controller scope, does not infer PCIe link details, and never converts Percentage Used into remaining health or a QC result. See phase-4-validation.md for conversion rules, actual INTEL results and remaining hardware coverage.

## Phase 5 assessment and history boundary

Core/Assessment derives a versioned application assessment from completed SATA or NVMe results while keeping native query outcomes and device-reported statuses intact. Severity and evidence coverage are independent. The diagnostic checklist requires all mandatory evidence for PASS, exposes each rule and missing input, and excludes performance, full-surface integrity and future reliability.

Core/History creates immutable snapshots containing device evidence, query outcomes/raw payloads, lossless metric text and the original assessment/rule/parser/schema versions. Automatic comparison requires a reliable combined identity key plus matching protocol, scope, units, source and timestamp order. Infrastructure/History stores snapshots and normalized child rows transactionally under LocalAppData with a two-day configurable default and asynchronous cleanup. Cancelled/failed attempts are explicit non-complete snapshots. History failure never removes the live protocol result.

Application/Reporting exports one selected live or historical snapshot. Serial redaction defaults on and raw payloads default off; JSON counter values remain decimal strings. The shell retains the two protocol tabs and hosts the paged history/filter/detail/export area below them. See phase-5-validation.md for the rule table, schema, actual INTEL results and remaining checks.

## Phase 6 benchmark boundary

Core/Benchmark defines `benchmark-policy-v1`, exact workload configuration, independent operation results, monotonic throughput/IOPS/latency calculations, completion states and strict comparison compatibility. Application/Benchmarking resolves a user-selected filesystem directory to current discovery evidence without treating a drive letter or disk number as persistent identity. Its explicit consent contract is keyed to the complete target/workload signature.

Infrastructure/Benchmarking uses only asynchronous `FileStream` operations within a cryptographically named application-owned file and manifest. It has no raw-device handle or protocol command dependency. Preparation uses deterministic nontrivial data. Every path validates size, blocks, iterations, random operation count, queue depth, maximum writes and free-space reserve before file creation. Cancellation stops scheduling, closes handles and performs ownership-verified cleanup; window close waits for that sequence.

SQLite schema 2 adds benchmark sessions transactionally beside the Phase 5 tables. Methodology comparison requires reliable target identity and identical scope/configuration. Benchmark reporting is separate from diagnostic reporting but retains serial redaction and lossless counter strings. The collapsible benchmark area sits below the unchanged SATA/NVMe tabs. See phase-6-validation.md.

## Phase 7 release boundary

The executable manifest is `asInvoker`, PerMonitorV2-aware and x64-published self-contained. The Inno Setup package installs per user under LocalAppData; application data remains in its existing LocalAppData location and is outside installer ownership. `build/release.ps1` runs the Release/UI gate before publish, rejects development artifacts, builds the installer and records SHA-256. Signing is intentionally absent until an organization-controlled certificate is available.

Benchmark ownership now combines an unpredictable manifest/name with the Windows volume serial and file index obtained from the opened handle. Target ancestors, the owned directory, manifest and data file must not be reparse points. The engine rechecks identity before preparation, each operation and deletion. See phase-7-validation.md.
