# Verification Space Fix - 2026-10-11

- Root cause: Phase6Tests line 106 expected two consent calls. Actual target space was about 3.2 GiB, so the production greater-than-10-GiB rule correctly refused start before consent. The assertion was environment dependent, not a broken consent flow.
- Added an internal target-resolution dependency defaulting to BenchmarkTargetResolver.Resolve. Production composition, real-space checking, consent, mapping and benchmark policies are unchanged.
- Disposable workflow tests explicitly inject simulated 20 GiB available-space metadata. The helper requires a PIXINIT-named directory under the system TEMP directory and at least 64 MiB actual space. Existing workload limits remain: at most 32 MiB legacy file preparation and 1 MiB sustained fixtures. No full physical benchmark or raw-device writes.
- Added regressions confirming the production resolver remains default and exactly 10 GiB refuses consent, produces no session and creates no benchmark file.
- Final build/release.ps1 verification passed all 374 unique checks: 344 behavioral checks and 30 WPF checks. Build zero warnings and errors. Earlier focused/full reruns overlap this total.
- Updated test fixtures: Phase6Tests.cs, Phase7Tests.cs, StorageSpecsTests.cs, CompanyWorkflowTests.cs and BenchmarkContextTests.cs. Additional regression checks: BenchmarkRevampTests.cs. Production change: BenchmarkViewModel.cs internal resolver dependency.
- Logs: artifacts/verification/verification-fix-build.log, verification-fix-tests.log and verification-fix-release.log.
- Normal benchmark startup remains blocked on this host until more than 10 GiB becomes available. Tests do not weaken this requirement. Installer installation, higher DPI and physical-drive performance qualification remain unverified.

## Rebuilt installer
- build/release.ps1 completed successfully: verification, self-contained Windows x64 publish and Inno Setup compilation.
- Path: artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe
- SHA-256: E5F5A4A139C49DC2C8383F7E0875EFFC560877A89A6EBDA6E1D9A978DD5BA730
- Unsigned. This exact installer was not installed or distributed.
