# Unbuffered benchmark validation

- Date: 2026-10-09. Release readiness: PARTIAL.
- Current implementation supersedes earlier buffered-only descriptions. Legacy buffered history and optional buffered mode remain supported.

## Latest timed workload pass

- Timed workload implemented, physical CDM comparison still required for accuracy claim.
- Default UI enables timed mode: 5 seconds, optionally 10 or 20 seconds per selected profile/pass. Unchecked uses the prior fixed operation/byte workload. Old JSON without TimedMode remains fixed mode.
- Stopwatch uses the high-resolution performance counter. Submissions stop at the deadline; pending I/O drains safely and the final write flush remains inside measured elapsed time. Slow completions can exceed the nominal duration; no elapsed time is forced to five seconds.
- Sequential offsets wrap within the selected 1/2/4 GiB file; random offsets stay in range. Reuse is disclosed instead of silently growing the consented file. The file is cryptographically prepared before timed writes as well as reads. Within each profile, write runs before read using the same retained file.
- The 96 GiB ordinary ceiling includes preparation. Remaining bytes are split across selected write jobs/passes, aligned to MiB. If a job exhausts its allowance before the submission deadline it fails as incomplete and does not contribute to average/best. A large number of fast timed writes cannot all finish under this ceiling. Sustained keeps its separate 32 GiB ceiling.
- AlignedBuffer allocates with VirtualAlloc and releases with VirtualFree. The engine checks GetDiskFreeSpaceW logical sector size, handle storage alignment, pointer alignment, file size, every block size and every offset. No buffered fallback.
- Consent now identifies the exact randomized owned file path and its size before creation. Collision handling preserves an existing file. The nonce-based naming and ownership checks remain in place instead of using a shared test_1GiB.dat name.
- Two-second cancellation-aware pause between passes. TRIM is explicitly logged as skipped, TrimAttempted=false: the file is retained for subsequent read-after-write tests. Trimming this range could invalidate read contents; a deleted file cannot receive file-level TRIM. No zero-data substitution, nonexistent FSCTL_DELETE_DATA fallback, volume flush or volume lock is issued. Final owned-file deletion leaves deallocation behavior to Windows, without claiming completion.
- Per-pass JSON includes IsValidForDeviceAttribution, IncludeInAverage, ElapsedSeconds and TotalBytes. Session JSON includes SectorSize, BufferAlignment, TrimAttempted and TrimStatus. Invalid SATA sequential attribution is excluded from averages/best, while original measurements remain visible. Unknown target mapping is separately marked uncertain.
- MB/s remains decimal; the requested division by 1048576 is displayed as MiB/s, not mislabeled MB/s. Concurrent IOPS uses completed operations / actual elapsed seconds.
- New files: Infrastructure/Benchmarking/AlignedBuffer.cs, Preconditioning.cs and tests/pixinit.Tests/AccurateResultTests.cs. Existing NativeBenchmarkFile and DirectBenchmarkEngine remain the implementation; no duplicate NativeIoEngine or orchestrator architecture was added.
- New bounded checks cover allocation alignment, file-size rejection, five-second circular read, decimal/binary units, 1 MiB write ceiling, cleanup, timing signatures/budget, skipped-TRIM logging, invalid-pass averaging, export fields, exact planned filename and collision preservation.
- First timed read verification: 5.0004 seconds, 1 MiB physical preparation; repeated reads stayed within that owned file. This is timing evidence, not a physical drive performance claim.
- Latest suite: 337 unique checks, including 12 new checks over the previous 325. No full physical write benchmark ran. The five-second read test uses a disposable 1 MiB file; its separate write-cap check writes at most 1 MiB.
- Final timed read verification: 5.0008 seconds, 512-byte logical sector and 4096-byte effective storage alignment.
- Final verification and packaging log: artifacts/verification/timed-release.log. Screenshots remain actual disconnected WPF harness windows. 125%/150% DPI and physical CrystalDiskMark comparison remain NOT RUN.

The earlier implementation notes below describe the fixed-workload baseline; the timed-mode details above take precedence.

## Fixed-workload baseline

- Windows x64 owned-file engine requests NO_BUFFERING, WRITE_THROUGH, OVERLAPPED and SEQUENTIAL_SCAN. Unsupported opens/alignment fail explicitly without buffered fallback.
- SafeHandle identity is verified before sizing or writing; compressed, sparse and reparse-point files are refused. Native requests are cancelled and drained before freeing aligned buffers. Existing manifest-based cleanup remains in place.
- Profiles: SEQ1M Q8T1, SEQ1M Q1T1, RND4K Q32T16 and RND4K Q1T1. Q is outstanding requests per worker; T is worker count. Normal Q32T16 permits 512 outstanding requests.
- Unbuffered defaults: 1 GiB, five passes. Choices: 1/2/4 GiB and 1/3/5/9 passes. Random profiles use 65,536 total operations per profile/pass. Workloads are fixed byte/operation counts, not CrystalDiskMark timed workloads.
- Cryptographic random preparation uses fresh bytes per MiB. Timed requests use distinct random buffers per outstanding slot and reuse those buffers. Preparation and allocation are outside measurement; completion drain and final write flush are included.
- Average is arithmetic mean of completed pass rates; best and every pass are retained. Aggregate IOPS uses completed operations divided by elapsed time, not inverse mean latency for concurrent workloads. MB/s is decimal; MiB/s is binary.
- Selected mapped drive root replaces AppData as the suggested target. Consent shows the exact directory, mapping, workload and application-byte ceiling. Target/configuration are revalidated after consent.
- Native ordinary application-write ceiling is 96 GiB, explicitly separate from the preserved legacy 3 GiB ceiling. A maximum ordinary configuration (4 GiB, nine passes, all profiles) plans 80.5 GiB including preparation. Sustained retains its separate 32 GiB cap, 1 GiB circular region and 30 second maximum. Space reserve is at least 3 GiB or 10 percent for native workloads, plus required file footprint.
- SATA sequential measurements above 600 MB/s are invalid for single SATA device attribution. Original measurements remain visible; comparisons and assessment exclude invalid attribution. No result is clamped to an expected score.
- SQLite JSON, text/JSON export, history and UI retain native methodology, selected profiles, per-pass results and validity warnings.

## Accuracy boundaries

- OS file-data cache bypass does not remove controller caches or prove durable NAND latency. Moving a buffered file out of AppData does not fix caching.
- A 1 to 4 GiB file is not necessarily larger than RAM. Unbuffered flags, sector alignment and actual completion timing implement the bypass request.
- A 500 ms pause does not guarantee SLC recovery and does not issue TRIM. Sustained throughput is byte/time weighted; a drop candidate does not prove SLC exhaustion.
- Raw physical-drive writes and volume locks are not used. Neither is necessary for this owned-file test.
- Negotiated PCIe speed is not supplied by NVMe Identify Controller. SATA word 76 reports capabilities, not proof of current negotiated throughput. Link negotiation remains unavailable unless separately evidenced.
- No Micron 2400 model/capacity limit has been verified. No model-specific score adjustment is implemented.
- Exact CrystalDiskMark 8 equivalence and +/-5 percent agreement are NOT VERIFIED and cannot be guaranteed. The requested profile combination is not identical to every CDM8 preset. No Kingston or Micron performance claim follows from these tests.

## Earlier verification

- Release build: 0 warnings, 0 errors. Executable harness with --ui: 325 unique checks passed (310 existing plus 15 new checks). These are not additive to earlier overlapping runs.
- New checks cover native flags, maximum budget arithmetic, invalid pass count, consent refusal, four-profile reads, concurrency metadata, bounded random writes, cancellation/cleanup, replaced-file refusal, average/best calculation, SATA plausibility, history/export, comparison compatibility and UI defaults.
- Physical benchmark validation was limited to disposable 1 MiB fixtures and bounded random-write cases. No full GiB benchmark or sustained production workload was run.
- Actual running WPF test-window captures: docs/screenshots/phase7-responsive-1366x768.png and docs/screenshots/phase7-responsive-desktop.png. These are disconnected UI harness windows, not live drive readings. Laptop content scrolls vertically.
- 125%/150% and mixed-DPI: NOT RUN. Screen reader: NOT RUN. Follow docs/installation.md manual scaling procedure.
- Final build, tests, publish and installer compiler output: artifacts/verification/unbuffered-release.log.
- Installer is unsigned. Installation, launch and uninstall of this newly rebuilt package: NOT RUN. Hardware CDM comparison, physical SATA, other controllers/filesystems and Windows 11: NOT RUN for this pass.

## Package

- Path: `artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe`
- SHA-256: `C55C0760E802E689BC4B76E3C37789E4929E3438E05A4462D7BAC94BC2DAE769`
- Self-contained Windows x64; compiler succeeded. Unsigned; this package has not been installed or distributed.

## Changed areas

- Core/Benchmark: configuration, budgets, profile/pass models and compatibility.
- Infrastructure/Benchmarking: native aligned I/O, profile orchestration and handle ownership verification.
- Application/Benchmarking and Application/Reporting: exact-target consent and honest exports/assessment.
- ViewModels/Benchmark and Views/Benchmark: defaults, profiles, passes, target/progress, averages and warnings.
- tests/pixinit.Tests: bounded native tests and explicit legacy mode in existing fixtures.
- No new NuGet dependencies. app.manifest and WebView2 setup unchanged.

## Primary references

- [Microsoft file buffering and alignment](https://learn.microsoft.com/en-us/windows/win32/fileio/file-buffering)
- [Microsoft flush and write-through semantics](https://learn.microsoft.com/en-us/windows/win32/fileio/flushing-system-buffered-i-o-data-to-disk)
- [CrystalDiskMark 8 release profiles](https://crystalmark.info/en/2020/11/20/crystaldiskmark-8-0-0/)


## Model-specific endurance rating

- Intel SKU SSDPEKNW512G8 is listed in Intel's SSD 660p family declaration. Intel's 660p brief rates its 512 GB capacity at 100 TBW. PIXINIT applies this rating only when both Windows-discovery model and NVMe Identify model match the SKU and capacity is within 2% of nominal 512 GB.
- The host-write count remains observed diagnostic evidence; percentage is remaining rated endurance budget by arithmetic, not a health prediction or failure threshold. Unknown models retain an unverified reference and do not receive this profile.
- Sources: [Intel SSD 660p product brief](https://www.intel.com/content/dam/www/public/us/en/documents/product-briefs/660p-series-brief.pdf), [Intel declaration listing SSDPEKNW512G8](https://www.intel.com/content/www/us/en/content-details/648106/solid-state-drives-ssd-sd1807030-intel-ssd-660p-series-m-2-solid-state-drive-declaration-of-conformity.html).
