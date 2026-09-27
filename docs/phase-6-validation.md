# Phase 6 — bounded filesystem performance benchmarking

Validated 2026-09-27. Phase 6 is complete. Phase 7 packaging/distribution work has not started. Physical SATA benchmark validation remains **NOT RUN**.

## Baseline and boundary

The Phase 5 baseline passed with a Release build at zero warnings/errors, 186 UI checks and 200 actual-hardware checks. Phase 6 preserves discovery, protocol diagnostics, assessment/checklist, diagnostic history/export and their lifecycle protections.

Benchmarking occurs only through an application-created file inside a selected filesystem directory. The engine has no physical-drive, partition, volume, ATA pass-through, NVMe protocol-write, firmware, TRIM, format, erase or partition code. The actual validation dispatched zero ATA commands and only the six pre-benchmark read-only NVMe diagnostic queries already used by Phase 5.

## `benchmark-policy-v1`

All workloads use buffered asynchronous .NET filesystem I/O, no write-through, queue depth 1, deterministic nontrivial SplitMix64-derived data, and an unpredictable owned filename. File creation/preparation, warm-up and cleanup are timed separately and excluded from measured throughput. Version 1 uses no separate warm-up workload (`0` seconds recorded).

| Preset | File | Sequential block | Random block | Iterations | Random operations | Operations | Maximum writes |
|---|---:|---:|---:|---:|---:|---|---:|
| Quick | 32 MiB | 1 MiB | 4 KiB | 1 | 1,024 | Sequential Write, Sequential Read, Random Write, Random Read | 68 MiB |
| Standard | 128 MiB | 1 MiB | 4 KiB | 2 | 8,192 | Same order | 448 MiB |
| Custom | 16–512 MiB | 4 KiB–8 MiB | 4 KiB–1 MiB | 1–4 | 1–65,536 | Any nonempty selection | At most 1 GiB |

Blocks must divide the file exactly. Queue depth is fixed at one for this policy. The maximum write calculation includes read-file preparation when a valid prepared owned file is unavailable, all sequential-write bytes, and all random-write bytes. There is no run-until-stopped mode.

Before file creation the engine reads filesystem free space. It reserves the greater of 1 GiB or 10% of available space in addition to the required new file allocation. Unsafe sizes, iteration counts, operation counts, alignment, write totals and insufficient space are rejected before an owned file is created.

## Consent and target identity

The first operation that writes in a session, including preparation needed by a read test, presents a Yes/No dialog. Nothing is preselected. It states the exact maximum bytes, finite flash endurance, temporary activity/temperature/power effects, major sources of variation, bounded-file behavior and absence of raw-sector writes. Consent is keyed to target path plus the complete workload signature. A target or material configuration change invalidates it. A valid existing PIXINIT-owned prepared file can support read-only measurement without further writes or consent; normal UI sessions clean files after every run.

The target resolver compares the filesystem root with current discovery mount points. Exactly one discovered device with serial, PnP instance and interface evidence enables physical-device history comparison. Ambiguous mappings remain safe filesystem tests but are not attributed to a persistent physical identity. A drive letter, disk number, model or serial alone is never considered sufficient. Controller-wide diagnostic values are not turned into namespace-specific benchmark evidence.

## Measurement methodology

Sequential operations traverse the exact bounded file with the configured block and iteration count. Random operations use a deterministic offset sequence aligned to the configured block and constrained to the owned file. Each completed operation is timed with the monotonic high-resolution `Stopwatch` clock. Results retain bytes, actual elapsed ticks converted to seconds, and operation counts.

- bytes/second = completed measured bytes / measured seconds
- MB/s = bytes/second / 1,000,000
- MiB/s = bytes/second / 1,048,576
- IOPS = completed random operations / measured seconds
- average/minimum/maximum latency = observed per-operation monotonic duration; sample count is retained

No percentile is reported. No random IOPS value is inferred from sequential throughput. The UI rounds values while SQLite and exports retain floating-point round-trip values and lossless decimal strings for byte/operation counters.

These are buffered filesystem measurements. Windows/filesystem/controller cache, compression or encryption, SSD SLC/write cache, thermal state, power mode, background applications, free space, firmware, test size and exact workload can materially change results. Cache elimination is not claimed. A run is not a health verdict or manufacturer-rated maximum.

## Owned file, cancellation and shutdown

Each run creates `.pixinit-benchmark/pixinit-benchmark-{session GUID}-{cryptographic nonce}.bin` plus an ownership manifest in the selected directory. Cleanup first verifies the complete manifest and exact path; it never enumerates and deletes files by extension. Normal completion, cancellation and operation failure close the stream and attempt verified deletion. Empty PIXINIT directories are removed.

Cancellation stops scheduling, returns a Cancelled/incomplete operation or preparation state, closes handles, saves partial session evidence, and cleans the file. Device/tab changes invalidate the generation and reject the late result. Window close is delayed while cancellation and verified cleanup finish. On cleanup failure the UI/log/session show the exact remaining path. On restart only manifest-verified orphans are eligible for retry cleanup.

## History migration, comparison and export

SQLite schema version 2 adds `benchmark_sessions` and indexes in a transaction. The Phase 5 `sessions`, `query_outcomes`, `metrics` and settings remain intact. Benchmark rows store immutable JSON with identity evidence, path/filesystem scope, complete policy/configuration, results, precise timing, consent state, cleanup state, failure reason and temperature context. The existing retention transaction removes only expired application diagnostic and benchmark rows. Migration failure never deletes the database; persistence failure leaves the live result visible and logs the cause.

Comparisons require completed sessions, reliable matching target, same filesystem root, file size, block sizes, iterations, random count, queue depth, buffering mode and increasing timestamps. Each compatible operation shows previous/current MB/s, absolute delta and percentage delta. The UI warns that small differences may not be statistically meaningful and makes no improvement/regression claim.

Benchmark TXT/JSON schema is `pixinit.benchmark-report/1.0`. Reports contain application/policy versions, UTC timestamps, target and identity evidence, filesystem, configuration, consent requirement/status, results, byte and operation counts, timing, IOPS, latency, available temperature context, completion/cleanup state, methodology and limitations. Serial redaction defaults on. Byte and operation counters are duplicated as documented lossless decimal strings. Test-data contents are never exported.

## Actual Intel NVMe validation

The production application ran non-elevated on Windows 10.0.19045 x64 against `INTEL SSDPEKNW512G8`, firmware `004C`. `%LocalAppData%` on `C:\` mapped to that single discovered device using current-session mount and identity evidence. Numeric NSID remained unavailable and no namespace value was inferred.

The explicit consent provider was exercised before the first write. One Quick run was cancelled during preparation and cleaned. Two Quick sessions then completed all four operations and cleaned. Across those three sessions the exact bytes written are recorded in `phase-6-hardware-observations.txt`; the configured upper bound was 176,160,768 bytes (two complete 68 MiB allowances plus one 32 MiB preparation allowance). No benchmark file or manifest remained.

The final completed run's exact sequential MB/s/MiB/s, random IOPS and average/minimum/maximum latency are recorded in `phase-6-hardware-observations.txt` and exported in `phase-6-intel-nvme-benchmark.txt` / `.json`. The values are measurements of that run under buffered filesystem/cache conditions, not permanent device capability. A valid pre-benchmark NVMe temperature is stored with its source/scope. Post-benchmark temperature remains unavailable because Phase 6 does not claim continuous monitoring or silently issue another diagnostic query.

A new SQLite store instance reopened two completed sessions and one cancelled session. All four compatible operations produced comparisons. Exported JSON byte results exactly matched the selected stored session and the serial was redacted. The TXT retained policy and methodology. ATA dispatch count remained zero.

## Verification and remaining work

Phase 6 adds **38 unique isolated checks** for calculations, monotonic timing, presets/limits, free space, consent, owned files, read-only reuse, completion/cancellation/failure/cleanup, restart recovery, safe cleanup retry, manifest path confinement, close and stale-result behavior, migration/rollback/history failures, diagnostic-history preservation, lossless values, compatible comparison and exports. The actual Intel path adds **14 unique Phase 6 checks** for mapping, bounds, consent, cancellation, four operations, cleanup, namespace withholding, restart/reopen, comparison, export fidelity and zero ATA writes.

Actual application screenshots:

- `screenshots/phase6-benchmark-config-laptop.png`
- `screenshots/phase6-benchmark-active-laptop.png`
- `screenshots/phase6-benchmark-completed-desktop.png`
- `screenshots/phase6-benchmark-history-desktop.png`

Before Phase 7, validate physical SATA, Windows 11, other controllers/filesystems, access-denied/read-only targets, BitLocker and other encryption stacks, low-free-space behavior on a disposable test volume, keyboard/screen-reader flows, high/mixed DPI and multiple monitors. A product decision is also needed on whether post-benchmark diagnostic refresh should be user-invoked or offered explicitly. No installer, signing or distribution work is included here.
