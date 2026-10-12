# Five Gap Validation - 2026-10-11

Update: the test-environment blocker described below was subsequently fixed using explicit disposable workflow fixtures. Final release verification passed 374 checks with zero warnings/errors. Production space refusal remains intact. See verification-space-fix.md. The earlier failure remains recorded as evidence.

## Implemented and retained behavior
- Read PROJECT_SUMMARY_FOR_AI.md. Reviewed the 12-page Downloads/PIXinit Software New Update.pdf text and representative rendered screenshot pages 1, 2, 8 and 12. Screenshots include SATA sessions, so the Intel NVMe rating is not applied to every pictured device.
- Diagnostic footer already follows current snapshot presence and measured host writes, including valid zero readings. Intel SSDPEKNW512G8 at matching 512 GB capacity and matching controller/discovery models uses the existing verified 100 TB manufacturer catalog. SMART provides written TB, not the manufacturer warranty rating. Unknown devices retain an explicitly unverified reference.
- Updated filtered write-average rule: completed eligible same-profile write below 0.5 GiB AND below 100 MB/s, with another valid same-profile write above 500 MB/s. Fixture 910.41/805.65/14.53 produces filtered 858.03 MB/s (2/3); original all-pass average 576.86 and Best 910.41 stay visible and unchanged in stored measurements. Throughput thresholds alone cannot prove SLC exhaustion, QLC identity or a defect.
- Sustained cap: existing greater-than-80-percent-full policy retained; absolute available space below 20 GiB also caps requested sustained duration to five seconds and write allowance to at most 16 GiB. Less than 10 percent free therefore also qualifies. Existing greater-than-10-GiB startup requirement and engine reserves remain. At 90 percent used or less than 20 GiB available, warning text is red. No guaranteed 13 or 900 MB/s claim.
- Session JSON records observed target-volume fullness and available bytes at start. Pass tooltips/slow-write warnings use recorded fullness, never current fullness attributed retroactively to history. Older sessions have null observations.
- Added BenchmarkProcessLease: checks DiskMark64, DiskMark32 and DiskMark64A processes before preparation and again after consent/before engine start. Blocks conservatively if CrystalDiskMark is open, even if idle. Cross-process PIXINIT lock at %TEMP%/PIXINIT_BENCHMARK_LOCK uses CreateNew, exclusive sharing and DeleteOnClose, held through the session and released on return, cancellation or exception. Existing/unowned lock paths are never overwritten, reclaimed or deleted. Different user TEMP locations are not one global machine lock; CrystalDiskMark does not honor the PIXINIT lock, and a later external start cannot be prevented.
- Mode badge uses saved observed PCIe generation/width or explicit link not reported. SATA 600 MB/s is labeled an interface ceiling, not a measured speed. Best read and sustained overall remain measured results. No speed-based link estimation.
- Existing Quick/Deep/Custom buttons, size pills, collapsed Advanced, single progress/ETA, TBW written-budget bar and compact temperature retained. Slow-pass warning is now one compact summary with at most three examples; all pass evidence remains in tooltips. Write consent remains enforced but its status line stays hidden. Phase duration is not whole-session duration.

## Verification
- dotnet build pixinit.slnx -c Release --artifacts-path artifacts/diagnostics-build: passed, zero warnings/errors.
- --revamp: 30 focused behavioral checks passed, including same-profile outliers, strict AND boundaries, Intel rating, observed-link badge, low-space cap, process-name detection, exclusive locks and deletion/preservation safety.
- --ui-only: 30 WPF UI checks passed; actual fixture client captures regenerated at 1366x768, 1800x1000 and 760x640. These are UI evidence, not physical speed evidence. 60 affected checks total, no overlap between these two modes.
- Full --ui suite attempted: passed checks through 224, then consent test could not start because the actual system volume has about 3.2 GiB free, below required 10 GiB. Full suite remains BLOCKED by environment; not claimed passed. No second filesystem volume available. Production safety was not bypassed, unrelated user files were not deleted and no full physical benchmark was run.
- build/release.ps1 stopped on that verification failure before packaging. Existing installer retained, not rebuilt or installed for this pass. Existing artifact SHA-256: BCE4505DEE5CA6011C40E569BCFF34A800022ADEB160584473595F0B4FE005BC. It does not contain this source update.
- Logs: artifacts/verification/five-gaps-build.log, five-gaps-focused.log, five-gaps-ui.log, five-gaps-tests.log and five-gaps-release.log.
- Remaining: free more than 10 GiB and rerun build/release.ps1; installer lifecycle, higher DPI, physical SATA/NVMe performance qualification and external-process-live detection remain unverified. Repeated PDF session failures/cleanup failures were not reproduced from screenshots; these changes do not assert their root causes.

## Changed files
- Infrastructure/Benchmarking/BenchmarkProcessLease.cs
- ViewModels/Benchmark/BenchmarkViewModel.cs, BenchmarkPresentationViewModel.cs and WritePassPresentation.cs
- Core/Benchmark/BenchmarkModels.cs (additive volume observations)
- Views/Benchmark/BenchmarkView.xaml
- tests/pixinit.Tests/BenchmarkRevampTests.cs
- Project summary, this report and regenerated fixture screenshots
