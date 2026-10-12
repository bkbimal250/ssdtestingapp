# Write Pass UI Validation - 2026-10-10

- Changed: BenchmarkPresentationViewModel.cs, new WritePassPresentation.cs, BenchmarkView.xaml, BenchmarkRevampTests.cs and project summary.
- Main write cards expose a separate filtered average, all-pass average, unchanged Best and retained/total pass count. Original pass records remain in history and exports; the all-profile section intentionally shows the original measured average.
- Filter rule: completed, eligible device-attributed write below 0.5 GiB or 50 MB/s with another valid write above 500 MB/s in the same operation/profile. Read, different-profile, uniformly slow and already excluded passes are not reclassified.
- Example fixture: 910.41, 805.65 and 14.53 MB/s gives filtered 858.03 over 2/3 passes, measured 576.86, Best 910.41. No original inclusion flag changed.
- Completed eligible write below 100 MB/s produces a warning below cards. It explains possible free-space effects without inventing historical volume fullness or diagnosing cache exhaustion. Current fullness remains in the target status. No guaranteed 900 MB/s recovery, verified QLC behavior or defect verdict is claimed.
- Sustained measurements and badge were not changed.
- Release command: dotnet build pixinit.slnx -c Release --artifacts-path artifacts/diagnostics-build. Passed: 0 warnings, 0 errors.
- Final suite: dotnet artifacts/diagnostics-build/bin/pixinit.Tests/release/pixinit.Tests.dll --ui. All 360 unique checks passed, including eight new pass-filter boundary/evidence checks. No physical benchmark run; existing bounded disposable tests only.
- Logs: artifacts/verification/pass-drop-build.log and pass-drop-tests.log. WPF harness regenerated fixture captures; no screenshot establishes cache exhaustion or physical speed.
- Installer not rebuilt or installed for this request; existing installer predates this source change. Higher DPI and additional physical controllers remain unverified.
