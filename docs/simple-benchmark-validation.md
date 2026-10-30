# Simplified benchmark validation

- Date: 2026-10-23.
- Scope: native WPF Benchmark UI and the shared unbuffered benchmark recipe. Diagnostics discovery, health parsers, manifest, dependencies and packaging configuration are unchanged.

## User interface

- Three selectors: Passes (1, 3, 5, 9), File size (512, 1024, 2048 MiB), mapped Target volume.
- Default: three passes, 1024 MiB temporary file, fixed five-second submission window per read/write test. Total run time includes eight tests per pass, preparation, flush/drain and inter-pass pauses.
- All runs four profile rows; each profile button runs that row only. A row-only run replaces the previous session so results from different settings or times are not mixed.
- All becomes Stop during preparation or execution. Configuration and profile buttons are disabled while the operation is active.
- Aligned Read / Write MB/s cells show arithmetic averages of completed valid passes and their pass counts. Results update after each pass; running averages are labeled Running.
- Hover over a result for Best, IOPS, mean pass completion latency, original speeds, transferred bytes, durations and failure/exclusion evidence.
- Removed from visible Benchmark UI: Quick/Deep/Custom, Advanced, custom guide, duration selector, workload checkboxes, sustained testing, duplicate performance cards, all-profile cards and duplicate TBW/temperature cards. Existing backend capabilities remain available for regression coverage, not user configuration. Current health and temperature remain in Diagnostics.
- Empty values are --, incomplete results are labeled, failed cleanup retains its recovery action. Slow valid passes stay in the primary average without speculative cache-exhaustion labels.
- Main settings wrap below All below 800 DIPs. Results and settings remain scrollable at 760x640, with no horizontal scrolling. Screenshots use empty-state fixtures, not hardware measurements.

## Automatic workload profiles

| Device protocol | Row 1 | Row 2 | Row 3 | Row 4 |
| --- | --- | --- | --- | --- |
| SATA / other storage | SEQ1M Q8T1 | SEQ1M Q1T1 | RND4K Q32T1 | RND4K Q1T1 |
| NVMe | SEQ1M Q8T1 | SEQ128K Q32T1 | RND4K Q32T16 | RND4K Q1T1 |

- Parameters follow the documented [CrystalDiskMark 8 standard and NVMe profile definitions](https://crystalmark.info/en/software/crystaldiskmark/crystaldiskmark-history/). The interface follows its compact [test count, size, target and result workflow](https://crystalmark.info/en/software/crystaldiskmark/crystaldiskmark-main-window/).
- Queue depth is per worker. NVMe random Q32T16 can have up to 512 requests outstanding; SATA Q32T1 has 32. Existing legacy enum values are preserved for old configurations; the two new profile flags use distinct bits.
- PIXINIT retains its own native engine and timing/flush methodology. Matching profile names is not proof of CrystalDiskMark score parity.

## Measurement and safety

- Owned local filesystem file only; positively verified handle identity, aligned buffers/offsets, unbuffered/write-through overlapped I/O, cryptographic random preparation, monotonic byte/time measurement and completion draining.
- The ordinary 96 GiB application-write ceiling is shared across timed tests. A fast profile can use the remaining allowance instead of failing at a small equal per-job allocation. Preparation and completed ordinary writes are deducted, aligned down to whole MiB. The total ceiling is unchanged.
- Reaching the ceiling stops the run and labels the incomplete measurement; it is not silently accepted into the average. Timed policy version is v3; the safety fingerprint identifies the shared budget.
- File allocation is separate from cumulative writes. The 1 GiB file is reused and removed after completion/cancellation; it does not grow to the 96 GiB cap.
- Exact-target consent, mapped physical identity checks, greater-than-10-GiB available-space preflight, free-space reserve, shared StorageOperationGate, cancellation/generation protection, competitor process lock and cleanup are retained.
- No automatic sustained test. Existing internal sustained policy and byte ceiling are unchanged.
- Implausible single-SATA sequential results remain invalid for attribution without clamping the recorded speed. Slow valid results are retained.
- No raw disk writes, volume locks, automatic TRIM, inferred PCIe link from speed, guaranteed cache recovery, durable NAND-speed claim or fabricated hardware scores.

## Changed files

| Area | Files |
| --- | --- |
| Core | pixinit/Core/Benchmark/DirectBenchmarkModels.cs; pixinit/Core/Benchmark/BenchmarkModels.cs |
| Engine | pixinit/Infrastructure/Benchmarking/DirectBenchmarkEngine.cs |
| View models | pixinit/ViewModels/Benchmark/SimpleBenchmarkViewModel.cs; BenchmarkViewModel.cs; DirectBenchmarkViewModel.cs; BenchmarkPresentationViewModel.cs |
| UI and consent | pixinit/Views/Benchmark/BenchmarkView.xaml; BenchmarkView.xaml.cs; pixinit/Application/Benchmarking/WriteConsent.cs |
| Tests | tests/pixinit.Tests/SimpleBenchmarkTests.cs; Program.cs; BenchmarkRevampTests.cs; UnbufferedBenchmarkTests.cs |
| Documentation | PROJECT_SUMMARY_FOR_AI.md; docs/custom-benchmark-guide.md; this file; benchmark screenshots |

## Verification

- Build: dotnet build pixinit.slnx -c Release --artifacts-path artifacts/diagnostics-build -warnaserror.
- Harness: dotnet artifacts/diagnostics-build/bin/pixinit.Tests/release/pixinit.Tests.dll --ui.
- Logs: artifacts/verification/simple-benchmark-build.log and simple-benchmark-tests.log.
- Final Release build: 0 warnings, 0 errors. All 410 checks pass: 377 behavior checks and 33 WPF UI checks, including 18 new recipe/measurement checks.
- New native profile tests use only a disposable 1 MiB prepared file and read operations. No full physical write benchmark was run.
- Screenshots: docs/screenshots/benchmark-cards-1366x768.png, benchmark-cards-1800x1000.png and benchmark-minimum.png, rendered by the actual WPF harness at host 96 DPI.
- Pending: physical SATA/NVMe comparison against matching CrystalDiskMark settings, additional controllers, 125/150 percent or mixed DPI, and client hardware verification.
- Updated self-contained win-x64 single-file app: artifacts/publish/win-x64/PIXINIT.exe, 66,051,097 bytes (66.05 MB). Publish log: artifacts/verification/simple-benchmark-publish.log. Size check PASS: application below 100 MB.
- Installer is not rebuilt or installed for this source/UI change. Existing installer is 61,109,845 bytes and predates it; its successful size check does not establish that it includes this update.
