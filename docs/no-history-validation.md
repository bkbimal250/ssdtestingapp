# No-history release validation

## Changes

- Removed Microsoft.Data.Sqlite PackageReference and SQLite NuGet source mappings. No direct application NuGet packages remain. No EF DbContext or migration existed in this project; no replacement database or file history was added.
- Deleted Infrastructure/History/SqliteHistoryStore.cs and ViewModels/History/HistoryViewModel.cs. Removed history popup, retention/filter/list controls, initialization, constructor services, diagnostic snapshot saves, benchmark insert/load and temperature update saves. Removed UNIQUE-constraint failure pathways by removing persistence rather than swallowing errors.
- BenchmarkSession remains a plain live measurement result record used by engines and presentation, not an ORM entity. Core snapshot identity DTOs remain for target attribution and existing pure comparison/serializer checks; they do not save files. Current results disappear on selection/reset/exit. No PDF/JSON/QR export UI restored.
- App, ShellViewModel and BenchmarkViewModel use manual constructor composition without history services. Shared StorageOperationGate, generation checks, cancellation, Quick/Deep/Custom, unbuffered flags, consent/revalidation, free-space checks, write caps and owned-file cleanup remain.
- Thermal updates use current provider observations and in-memory thermal context; no SaveBenchmarkAsync is invoked after capture. Operational logging now writes only TEMP/PIXINIT_detect.log (with bounded rollover). Legacy databases/logs already on user machines are neither opened nor deleted.
- Changed application project, NuGet.Config, App.xaml.cs, MainWindow.xaml, shell/benchmark VMs, DiscoveryLog, release script, test harness and documentation. Database-specific tests retired, bounded live-result checks added. Release packaging rejects SQLite dependencies in the runtime dependency manifest.

## Actual verification

| Check | Result |
| --- | --- |
| Final Release build | 0 warnings, 0 errors |
| Final applicable harness with WPF UI | 396 unique checks passed |
| Focused no-persistence checks | 12 passed; included in final total |
| Bounded native no-persistence test | One MiB disposable file, unbuffered reads, at most one MiB preparation writes, Completed and cleaned |
| Repeated thermal updates | Current 30 C then 31 C context displayed with no persistence error |
| Assembly dependencies/types | No SQLite assembly/store/context/history VM |
| Runtime dependency manifest | Zero SQLite libraries |
| Compressed executable bundle library names | Microsoft.Data.Sqlite.dll, SQLitePCLRaw.core.dll, e_sqlite3.dll, SQLite.Interop.dll absent |
| Publish | PIXINIT.exe only; 66,054,209 bytes (66.05 MB) |
| Installer | 61,116,891 bytes (61.12 MB); size check PASS below 100 MB |

Commands: build/release.ps1, build/size-check.ps1 (also invoked by release), focused harness --no-persistence-only. Logs: artifacts/verification/no-sqlite-build.log, no-persistence-focused.log, no-sqlite-release.log. Existing bounded harness tests ran; no full physical benchmark was used for UI validation.

Installer: artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe.

SHA-256: B883C829C65DB9052ABD5725255857CDC3C2CFF07353B0621705416603EA3CC3.

## Limits and handoff

- Self-contained pure WPF .NET 8, no Chrome or SQLite. Supported compressed single-file publish retained; WPF trimming remains disabled. Publish shrank by 1,207,433 bytes from the preceding build, not to the predicted 58 MB because the .NET/WPF runtime dominates. Extracted runtime footprint may exceed compressed file size.
- Installer unsigned; install/upgrade/uninstall and client Windows 11 three-disk benchmark NOT RUN. Existing installed SQLite native DLLs from older versions may remain on disk but are not referenced by this new build; no broad user-data deletion or installer history deletion was introduced.
- Four-source discovery, deduplication, heuristic labels and generic physical overview preserved. Vendor USB/SAT, proprietary Intel/AMD RAID SMART and SCSI inquiry scan remain unimplemented/unverified; removal of SQLite does not establish universal controller support.
- Supplied example EVM/SATA/NVMe readings, E: target and speeds are not fabricated or hardcoded as output. Missing telemetry remains unavailable. Multi-PC hardware matrix pending; test fixtures are not proof of real client hardware.
