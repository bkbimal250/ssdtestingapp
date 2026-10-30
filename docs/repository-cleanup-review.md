# GitHub repository cleanup review

- Date: 2026-10-30.
- Repository: https://github.com/bkbimal250/ssdtestingapp.git; branch: main.
- Inventory: 2257 local files excluding .git; 257 files tracked before cleanup.
- Removed from Git tracking only: 49 generated files, 64,786,980 bytes (64.79 MB).
- All original local files still exist. All 49 removed-file SHA-256 hashes match the pre-cleanup inventory. Other original local-file sizes are unchanged, except the intentionally edited .gitignore.
- No application source/resource/dependency was deleted. No commit, push, reset, history rewrite or force push was performed.

## Files kept

| Category | Preserved files |
| --- | --- |
| Application | C# and XAML under pixinit/, project file, manifest, launch settings |
| Assets | Application icon and WPF styles/themes/resources |
| Tests | tests/ source, project configuration and documentation |
| Configuration | pixinit.slnx, NuGet.Config, .gitignore; lock files and .env.example templates remain eligible |
| Build source | build/release.ps1, build/verify.ps1, build/size-check.ps1, run.ps1 |
| Installer source | installer/pixinit.iss; generated installers are excluded |
| Automation | .github/workflows/dotnet-windows.yml |
| Documentation | README files, project summary, design/architecture/validation notes, hardware observation notes, documentation screenshots, SSD Specifications (1).pdf |

- build/ and installer/ contain essential release configuration. Their PowerShell and Inno Setup source files are allowlisted while generated outputs are excluded.
- Current .NET projects have no direct PackageReference or required checked-in binary dependencies. Restore/build supplies generated assemblies/runtime dependencies.
- Four pre-existing untracked source/documentation files remain eligible and are included in the manifest. The final git add -A command includes them.
- Package lock files are no longer ignored. No lock file or environment template currently exists in the tree.
- Documentation screenshots are retained as validation resources. Disposable PDF renders, proof exports and raw verification/machine telemetry outputs are untracked.
- Historical docs may reference generated output paths that will no longer exist in fresh clones; these files remain locally available.

## Ignored files and folders

| Category | Rules |
| --- | --- |
| Outputs/dependencies | bin/, obj/, node_modules/, dist/, out/, release/, artifacts/, publish/, packages/; build/ and installer/ outputs with source exceptions above |
| Binaries/bundles | *.exe, *.msi, *.msix, *.msixbundle, *.appx, *.appxbundle, *.dll, *.pyd, *.pdb, *.nupkg, *.snupkg, *.zip, *.7z, *.tar, *.gz |
| Local secrets | .env, .env.* except .env.example and .env.*.example; secrets.json |
| Caches/logs/results | .cache/, .vite/, coverage/, TestResults/, __pycache__/, *.py[cod], *.egg-info/, *.log, coverage files |
| IDE | .vs/, .vscode/, .idea/, user options, ReSharper caches, editor/debug history |
| Runtime data | *.db, *.sqlite, *.sqlite3 and DB sidecars; schemas and migration source remain eligible |
| Temporary/OS | *.tmp, *.temp, *.bak, swap files, backup suffixes, .DS_Store, Thumbs.db, Desktop.ini |
| Raw evidence | Explicit docs/phase baseline/behavior/hardware/verification output patterns and phase 5/6 machine exports |

## Files removed from Git tracking

Every file below remains on the local disk:
- `artifacts/installer/INSTALL.txt`
- `artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe`
- `artifacts/installer/SHA256SUMS.txt`
- `artifacts/verification/complete-protected-hashes.json`
- `artifacts/verification/installed-ui-helpers.ps1`
- `artifacts/verification/lightweight-launch-pid.txt`
- `artifacts/verification/pdf-qa-tools/zxing_cpp-3.1.1.dist-info/INSTALLER`
- `artifacts/verification/pdf-qa-tools/zxing_cpp-3.1.1.dist-info/METADATA`
- `artifacts/verification/pdf-qa-tools/zxing_cpp-3.1.1.dist-info/RECORD`
- `artifacts/verification/pdf-qa-tools/zxing_cpp-3.1.1.dist-info/REQUESTED`
- `artifacts/verification/pdf-qa-tools/zxing_cpp-3.1.1.dist-info/WHEEL`
- `artifacts/verification/pdf-qa-tools/zxing_cpp-3.1.1.dist-info/licenses/LICENSE`
- `artifacts/verification/pdf-qa-tools/zxingcpp/__init__.py`
- `artifacts/verification/pdf-qa-tools/zxingcpp/__pycache__/__init__.cpython-312.pyc`
- `artifacts/verification/pdf-qa-tools/zxingcpp/py.typed`
- `artifacts/verification/pdf-qa-tools/zxingcpp/zxingcpp.pyd`
- `artifacts/verification/pdf-qa-tools/zxingcpp/zxingcpp.pyi`
- `artifacts/verification/pdf-review/page-01.png`
- `artifacts/verification/pdf-review/page-02.png`
- `artifacts/verification/pdf-review/page-03.png`
- `artifacts/verification/pdf-review/page-04.png`
- `artifacts/verification/pdf-review/page-05.png`
- `artifacts/verification/pdf-review/page-06.png`
- `artifacts/verification/pdf-review/page-07.png`
- `artifacts/verification/pdf-review/page-08.png`
- `artifacts/verification/pdf-review/page-09.png`
- `artifacts/verification/pdf-review/page-10.png`
- `artifacts/verification/pdf-review/page-11.png`
- `artifacts/verification/pdf-review/page-12.png`
- `artifacts/verification/resale-proof/test-only-resale-proof.evidence.json`
- `artifacts/verification/resale-proof/test-only-resale-proof.pdf`
- `artifacts/verification/resale-proof/test-only-resale-proof.pdf.sha256`
- `artifacts/verification/resale-proof/test-only-resale-proof.png`
- `docs/phase-2-verification-output.txt`
- `docs/phase-3-baseline-output.txt`
- `docs/phase-3-behavior-output.txt`
- `docs/phase-3-verification-output.txt`
- `docs/phase-4-baseline-output.txt`
- `docs/phase-4-behavior-output.txt`
- `docs/phase-4-hardware-output.txt`
- `docs/phase-4-verification-output.txt`
- `docs/phase-5-baseline-output.txt`
- `docs/phase-5-intel-nvme.json`
- `docs/phase-5-intel-nvme.txt`
- `docs/phase-5-verification-output.txt`
- `docs/phase-6-baseline-output.txt`
- `docs/phase-6-intel-nvme-benchmark.json`
- `docs/phase-6-intel-nvme-benchmark.txt`
- `docs/verification-output.txt`

## Complete kept-file manifest

Existing tracked files plus eligible untracked project files and this report:

- `.github/workflows/dotnet-windows.yml`
- `.gitignore`
- `build/release.ps1`
- `build/size-check.ps1`
- `build/verify.ps1`
- `docs/architecture.md`
- `docs/baseline/MainWindow.phase2-before.xaml.txt`
- `docs/benchmark-ui-revamp-validation.md`
- `docs/bulk-no-export-validation.md`
- `docs/company-workflow-validation.md`
- `docs/compatibility.md`
- `docs/custom-benchmark-guide.md`
- `docs/diagnostic-layout-validation.md`
- `docs/diagnostics-ui-validation.md`
- `docs/enterprise-raid-validation.md`
- `docs/five-gaps-validation.md`
- `docs/full-data-permissions-validation.md`
- `docs/generic-storage-validation.md`
- `docs/installation.md`
- `docs/lightweight-release-validation.md`
- `docs/media-evidence-ui-validation.md`
- `docs/multi-disk-validation.md`
- `docs/no-history-validation.md`
- `docs/other-pc-benchmark-validation.md`
- `docs/phase-1-validation.md`
- `docs/phase-2-hardware-observations.txt`
- `docs/phase-2-validation.md`
- `docs/phase-3-hardware-observations.txt`
- `docs/phase-3-validation.md`
- `docs/phase-4-hardware-observations.txt`
- `docs/phase-4-validation.md`
- `docs/phase-5-hardware-observations.txt`
- `docs/phase-5-validation.md`
- `docs/phase-6-hardware-observations.txt`
- `docs/phase-6-validation.md`
- `docs/phase-7-validation.md`
- `docs/post-phase-7-ui-validation.md`
- `docs/raid-probing-validation.md`
- `docs/release-2026-10-08.md`
- `docs/release-2026-10-09.md`
- `docs/release-checklist.md`
- `docs/remove-device-overview-validation.md`
- `docs/repository-cleanup-review.md`
- `docs/resale-evidence-validation.md`
- `docs/roadmap.md`
- `docs/screenshots/benchmark-cards-1366x768.png`
- `docs/screenshots/benchmark-cards-1800x1000.png`
- `docs/screenshots/benchmark-minimum.png`
- `docs/screenshots/details-nvme-1366x768.png`
- `docs/screenshots/details-nvme-1800x1000.png`
- `docs/screenshots/details-nvme-thermal-desktop.png`
- `docs/screenshots/diagnostic-layout-desktop.png`
- `docs/screenshots/diagnostic-layout-laptop.png`
- `docs/screenshots/diagnostic-layout-minimum.png`
- `docs/screenshots/diagnostics-nvme-details-desktop.png`
- `docs/screenshots/diagnostics-nvme-smart-desktop.png`
- `docs/screenshots/installed-history-1800x1000.png`
- `docs/screenshots/installed-nvme-desktop-1800x1000.png`
- `docs/screenshots/installed-nvme-laptop-1366x768.png`
- `docs/screenshots/installed-sata-empty-1800x1000.png`
- `docs/screenshots/nvme-desktop.png`
- `docs/screenshots/nvme-laptop.png`
- `docs/screenshots/other-unidentified-tab-desktop.png`
- `docs/screenshots/phase2-desktop.png`
- `docs/screenshots/phase2-laptop.png`
- `docs/screenshots/phase3-desktop.png`
- `docs/screenshots/phase3-laptop.png`
- `docs/screenshots/phase3-sata-empty.png`
- `docs/screenshots/phase4-nvme-desktop.png`
- `docs/screenshots/phase4-nvme-laptop.png`
- `docs/screenshots/phase5-assessment-laptop.png`
- `docs/screenshots/phase5-history-desktop.png`
- `docs/screenshots/phase6-benchmark-active-laptop.png`
- `docs/screenshots/phase6-benchmark-completed-desktop.png`
- `docs/screenshots/phase6-benchmark-config-laptop.png`
- `docs/screenshots/phase6-benchmark-history-desktop.png`
- `docs/screenshots/phase7-responsive-1366x768.png`
- `docs/screenshots/phase7-responsive-desktop.png`
- `docs/screenshots/post-phase7-nvme-1366x768.png`
- `docs/screenshots/post-phase7-nvme-desktop.png`
- `docs/screenshots/sata-desktop.png`
- `docs/screenshots/sata-laptop.png`
- `docs/screenshots/sata-minimum.png`
- `docs/screenshots/smart-nvme-1366x768.png`
- `docs/screenshots/smart-nvme-1800x1000.png`
- `docs/screenshots/test-only-long-identity.png`
- `docs/screenshots/workspace-history-desktop.png`
- `docs/screenshots/workspace-other-desktop.png`
- `docs/simple-benchmark-validation.md`
- `docs/ssd-specifications-validation.md`
- `docs/storage-overview-validation.md`
- `docs/ui-responsive-review-2026-10-09.md`
- `docs/unbuffered-benchmark-validation.md`
- `docs/verification-space-fix.md`
- `docs/workspace-tabs-validation.md`
- `docs/write-pass-ui-validation.md`
- `installer/pixinit.iss`
- `NuGet.Config`
- `pixinit.slnx`
- `pixinit/app.manifest`
- `pixinit/App.xaml`
- `pixinit/App.xaml.cs`
- `pixinit/Application/Assessment/HealthAssessor.cs`
- `pixinit/Application/Benchmarking/BenchmarkTargetResolver.cs`
- `pixinit/Application/Benchmarking/WriteConsent.cs`
- `pixinit/Application/Discovery/DiscoveryCoordinator.cs`
- `pixinit/Application/Reporting/BenchmarkReportExporter.cs`
- `pixinit/Application/Reporting/DiagnosticReportExporter.cs`
- `pixinit/Application/Reporting/SatisfactionReportExporter.cs`
- `pixinit/Application/Scanning/NvmeOperationCoordinator.cs`
- `pixinit/Application/Scanning/SataOperationCoordinator.cs`
- `pixinit/Application/Scanning/StorageOperationGate.cs`
- `pixinit/Application/Selection/DeviceSelection.cs`
- `pixinit/AssemblyInfo.cs`
- `pixinit/Assets/pixinit.ico`
- `pixinit/Core/Abstractions/StorageProviders.cs`
- `pixinit/Core/Assessment/AssessmentPolicies.cs`
- `pixinit/Core/Assessment/HealthAssessment.cs`
- `pixinit/Core/Benchmark/BenchmarkModels.cs`
- `pixinit/Core/Benchmark/BenchmarkPolicy.cs`
- `pixinit/Core/Benchmark/DirectBenchmarkModels.cs`
- `pixinit/Core/Devices/StorageDevice.cs`
- `pixinit/Core/Diagnostics/Common/ManufacturerEnduranceCatalog.cs`
- `pixinit/Core/Diagnostics/Common/Metric.cs`
- `pixinit/Core/Diagnostics/Common/StorageUsage.cs`
- `pixinit/Core/Diagnostics/Common/ThermalInfo.cs`
- `pixinit/Core/Diagnostics/Nvme/NvmeDiagnostics.cs`
- `pixinit/Core/Diagnostics/Nvme/NvmeParsers.cs`
- `pixinit/Core/Diagnostics/Nvme/NvmeResultMapper.cs`
- `pixinit/Core/Diagnostics/Sata/AtaIdentifyParser.cs`
- `pixinit/Core/Diagnostics/Sata/AtaModels.cs`
- `pixinit/Core/Diagnostics/Sata/SataDiagnostics.cs`
- `pixinit/Core/Diagnostics/Sata/SataResultMapper.cs`
- `pixinit/Core/Diagnostics/Sata/SmartParsers.cs`
- `pixinit/Core/History/DiagnosticSnapshot.cs`
- `pixinit/Infrastructure/Benchmarking/AlignedBuffer.cs`
- `pixinit/Infrastructure/Benchmarking/BenchmarkProcessLease.cs`
- `pixinit/Infrastructure/Benchmarking/DirectBenchmarkEngine.cs`
- `pixinit/Infrastructure/Benchmarking/FileBenchmarkEngine.cs`
- `pixinit/Infrastructure/Benchmarking/NativeBenchmarkFile.cs`
- `pixinit/Infrastructure/Benchmarking/OwnedBenchmarkFile.cs`
- `pixinit/Infrastructure/Benchmarking/Preconditioning.cs`
- `pixinit/Infrastructure/Benchmarking/SustainedWriteEngine.cs`
- `pixinit/Infrastructure/Logging/DiscoveryLog.cs`
- `pixinit/Infrastructure/Windows/Discovery/DescriptorParser.cs`
- `pixinit/Infrastructure/Windows/Discovery/PcieLinkReader.cs`
- `pixinit/Infrastructure/Windows/Discovery/RaidDisplayFallback.cs`
- `pixinit/Infrastructure/Windows/Discovery/StorageQueryService.cs`
- `pixinit/Infrastructure/Windows/Discovery/UnavailableDiscovery.cs`
- `pixinit/Infrastructure/Windows/Discovery/WindowsDiskDiscovery.cs`
- `pixinit/Infrastructure/Windows/Discovery/WmiValue.cs`
- `pixinit/Infrastructure/Windows/Interop/StorageNative.cs`
- `pixinit/Infrastructure/Windows/Nvme/WindowsNvmeDiagnosticsProvider.cs`
- `pixinit/Infrastructure/Windows/Nvme/WindowsNvmeTransport.cs`
- `pixinit/Infrastructure/Windows/Sata/AtaCommandPolicy.cs`
- `pixinit/Infrastructure/Windows/Sata/WindowsAtaTransport.cs`
- `pixinit/Infrastructure/Windows/Sata/WindowsSataDiagnosticsProvider.cs`
- `pixinit/MainWindow.xaml`
- `pixinit/MainWindow.xaml.cs`
- `pixinit/pixinit.csproj`
- `pixinit/Properties/AssemblyInfo.cs`
- `pixinit/Properties/launchSettings.json`
- `pixinit/Resources/Styles/Controls.xaml`
- `pixinit/Resources/Styles/Diagnostics.xaml`
- `pixinit/Resources/Themes/Light.xaml`
- `pixinit/ViewModels/Benchmark/BenchmarkPresentationViewModel.cs`
- `pixinit/ViewModels/Benchmark/BenchmarkViewModel.cs`
- `pixinit/ViewModels/Benchmark/DirectBenchmarkViewModel.cs`
- `pixinit/ViewModels/Benchmark/SimpleBenchmarkViewModel.cs`
- `pixinit/ViewModels/Benchmark/WritePassPresentation.cs`
- `pixinit/ViewModels/Nvme/NvmeDetailsViewModel.cs`
- `pixinit/ViewModels/Nvme/NvmeViewModel.cs`
- `pixinit/ViewModels/Sata/SataViewModel.cs`
- `pixinit/ViewModels/Shared/DeviceStorageViewModel.cs`
- `pixinit/ViewModels/Shared/DeviceThermalViewModel.cs`
- `pixinit/ViewModels/Shared/DeviceViewModel.cs`
- `pixinit/ViewModels/Shared/ObservableObject.cs`
- `pixinit/ViewModels/Shell/ShellViewModel.cs`
- `pixinit/Views/Benchmark/BenchmarkView.xaml`
- `pixinit/Views/Benchmark/BenchmarkView.xaml.cs`
- `pixinit/Views/Nvme/NvmeView.xaml`
- `pixinit/Views/Nvme/NvmeView.xaml.cs`
- `pixinit/Views/Sata/SataView.xaml`
- `pixinit/Views/Sata/SataView.xaml.cs`
- `pixinit/Views/Shared/FirstPageView.xaml`
- `pixinit/Views/Shared/FirstPageView.xaml.cs`
- `pixinit/Views/Shared/MetricCard.xaml`
- `pixinit/Views/Shared/MetricCard.xaml.cs`
- `PROJECT_SUMMARY_FOR_AI.md`
- `README.md`
- `README.PROJECT.md`
- `run.ps1`
- `SSD Specifications (1).pdf`
- `SYSTEM_DESIGN_SDE2.md`
- `tests/pixinit.Tests/AccurateResultTests.cs`
- `tests/pixinit.Tests/BenchmarkContextTests.cs`
- `tests/pixinit.Tests/BenchmarkRevampTests.cs`
- `tests/pixinit.Tests/CompanyWorkflowTests.cs`
- `tests/pixinit.Tests/DiscoveryTests.cs`
- `tests/pixinit.Tests/HardwareTests.cs`
- `tests/pixinit.Tests/NoPersistenceTests.cs`
- `tests/pixinit.Tests/NvmeTests.cs`
- `tests/pixinit.Tests/Phase5Tests.cs`
- `tests/pixinit.Tests/Phase6Tests.cs`
- `tests/pixinit.Tests/Phase7Tests.cs`
- `tests/pixinit.Tests/pixinit.Tests.csproj`
- `tests/pixinit.Tests/Program.cs`
- `tests/pixinit.Tests/SataTests.cs`
- `tests/pixinit.Tests/SimpleBenchmarkTests.cs`
- `tests/pixinit.Tests/StorageOverviewTests.cs`
- `tests/pixinit.Tests/StorageSpecsTests.cs`
- `tests/pixinit.Tests/UnbufferedBenchmarkTests.cs`
- `tests/README.PROJECT.md`

## Verification and publishing

- No ignored path remains tracked after cleanup.
- No EXE/MSI/DLL/native Python module, artifacts/, bin/ or obj/ path remains in the next index snapshot.
- All essential tracked source, assets, scripts and workflow files remain tracked and unchanged.
- Ignore behavior verified for binary outputs, nested output directories, environment secrets, caches, package lock files, environment examples, scripts and required assets.
- Build/tests were not rerun because no application code changed, avoiding modification of local build outputs.
- At verification origin/main and local HEAD were both 1f974e4104ac57c94b32d7b2a0841d77055e4f1f. No unpublished local commits were found.
- Previously published binaries remain in Git history. Removing them from future source snapshots does not reduce existing historical objects or clone size. History is intentionally preserved.

Windows CMD commands, after reviewing this report:

```cmd
cd /d "C:\Users\ADMIN\Desktop\Bimal Works\Bimal Works\SSD\pixinit"
git add -A
git diff --cached --check
git diff --cached --stat
git diff --cached --name-status
git status --short
git commit -m "Keep source and build configuration; exclude generated outputs"
git push origin main
```

Review the staged manifest before committing. Git deletions are tracking removals; the local files are intact. If the remote changes and rejects the push, stop and review the incoming commits; do not force push.
