# Other-PC Benchmark Failure and Space Usage - 2026-10-12

## Evidence and limits
- User screenshots show NVMe Gen4 x4, a OneDrive/Desktop target, 512 MiB selected, all four profiles, three timed passes and sustained enabled. Session stopped after 5.4 seconds at 4 percent with 8.46 GiB cumulative application writes, Failed and temporary file cleaned. Available space displayed was 326 GiB before the run.
- Code divides the 96 GiB ordinary write budget across 12 write jobs: floor((96 GiB - 512 MiB) / 12) at MiB precision, plus preparation, matches approximately 8.46 GiB. Hitting this ceiling before a five-second submission window marks that write incomplete and aborts later profiles. This is a strong explanation consistent with the screenshot, not proof of the remote failure. Its actual session reason was not supplied.
- 8.46 GiB is cumulative bytes written while reusing the file, not retained file size. With sustained selected the old configuration required a 1 GiB file. The screenshot reports cleanup success but cannot establish cloud copies, recycle-bin contents, or actual later volume usage. No remote disk was inspected and no files on that PC were deleted.

## Changes
- Quick now uses one fixed-workload pass, a 512 MiB reusable file, all four profiles and 1000 random operations per profile, with sustained off. Maximum cumulative writes are approximately 1.51 GiB and required benchmark-file allocation is 0.50 GiB. The fixed workload does not use the timed write-ceiling path. It is a short workload result, not a sustained-performance claim or CDM-equivalent timed measurement.
- Deep remains an explicit larger timed workload; Custom exposes a timed-profile checkbox. Existing byte limits and consent remain. No cap increase or fabricated speed was introduced.
- UI shows temporary file size separately from maximum cumulative writes. Workload description identifies fixed versus timed mode and whether sustained is enabled.
- Default target uses local application data/PIXINIT_TEST on the selected volume, or selected-volume/PIXINIT_TEST. Redirected Desktop is no longer the default. OneDrive environment roots and standard OneDrive folder components are refused before benchmark creation. This is conservative and is not detection of every possible sync provider.
- Failed/cancelled sessions expose the saved reason and cleanup status. Incomplete attempted passes are labeled incomplete. Failed badge directs to details instead of combining Not run/average text. Cleanup failure includes the owned path and the existing Retry owned-file cleanup command.
- Reviewed existing owned-file cleanup: validates manifest and file identity before delete-on-close; arbitrary recursive cleanup was not added. Native and failure-test cleanup coverage remains in the suite. Old files on an external PC require verified ownership; unknown files are preserved.

## Verification
- Release build: zero warnings and errors. All 379 unique checks passed (349 behavioral and 30 WPF). Repeated release verification overlaps this total.
- Added assertions for bounded Quick configuration without executing a full workload, refusal of OneDrive paths, local suggested target, failure reason visibility and owned cleanup-path presentation.
- Existing tests use bounded disposable files, including 1 MiB native safety fixtures. No full physical benchmark was run and no diagnostic reads were added.
- Actual WPF fixture captures regenerated, including docs/screenshots/phase7-responsive-1366x768.png and phase7-responsive-desktop.png. Laptop capture visually checked for the new 0.50 GiB file / 1.51 GiB write summary and sustained-off default.
- Logs: artifacts/verification/other-pc-build.log, other-pc-tests.log and other-pc-release.log.
- Remote laptop root cause, real-device performance, cloud retained copies, higher DPI and installer lifecycle remain unverified. The unverified 600 TB reference for an unidentified model is not replaced with Intel's 100 TB rating without matching model evidence.

## Source
- ViewModels/Benchmark/BenchmarkPresentationViewModel.cs, BenchmarkViewModel.cs and DirectBenchmarkViewModel.cs
- Views/Benchmark/BenchmarkView.xaml
- tests/pixinit.Tests/BenchmarkRevampTests.cs and UnbufferedBenchmarkTests.cs
- Project summary, this report and generated captures

## Installer
- Final build/release.ps1 completed verification, self-contained Windows x64 publish and Inno Setup compilation.
- Path: artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe
- SHA-256: EAFF129B3817963286DECDE83CA6F77226DB43675B7EA4F90BB7F2EDD672C1CE
- Unsigned; this exact installer has not been installed or distributed.
