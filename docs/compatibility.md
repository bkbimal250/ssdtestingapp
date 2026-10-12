# Current compatibility and validation matrix

Current lightweight release (2026-10-12): pure WPF self-contained .NET 8, compressed single PIXINIT.exe 67.26 MB, installer 62.32 MB, 382 checks and zero warnings/errors. Browser removed; PARTIAL pending isolated installer/upgrade and Windows 11 mixed disks. See [validation](lightweight-release-validation.md). Earlier entries describe earlier packages.

Current update (2026-10-10): responsive benchmark UI and PCIe 3.0 x4 observed through Windows PnP on the Intel development-host NVMe. Physical speed agreement with CrystalDiskMark, other controllers/filesystems and 125%/150% DPI remain unverified. See [latest validation](benchmark-ui-revamp-validation.md).

Updated 2026-10-10. This describes the current release candidate. Earlier phase reports contain historical evidence, not guarantees for this build.

| Configuration or behavior | Current evidence |
| --- | --- |
| Windows x64 Release build and self-contained publish | Verified on the development host |
| Automated behavior and WPF harness | 352 unique checks; bounded disposable write tests only |
| 1366 x 768 and 1800 x 1000 at 96 DPI | Actual running WPF client captures; injected fixtures |
| 125 percent and 150 percent display scaling | NOT RUN |
| Mixed DPI, multi-monitor movement, Narrator | NOT RUN |
| Current installer install, launch, upgrade, uninstall | NOT RUN in isolation |
| Direct Intel NVMe on Windows 10 build 19045 | Earlier phase hardware evidence only; not rerun for this package |
| Physical SATA, additional NVMe controllers, numeric namespace mapping | NOT RUN; discovery does not establish a numeric NSID |
| Windows 11 and other Windows servicing baselines | NOT RUN |
| USB, RAID, bridges, virtual disks | Metadata/unknown-device presentation exists; diagnostic passthrough not established |
| ReFS, FAT/exFAT, network/removable, encrypted/tiered filesystems | Unverified; only earlier host NTFS evidence exists |
| x86, ARM64, macOS and Linux | No support claim; native storage boundary requires Windows x64 |
| Live WebView2 on other machines | NOT RUN; native WPF fallback retained |

- Current manifest and installer request administrator privileges. Earlier non-elevated/per-user installer results do not qualify the current permission model.
- History/settings use LocalAppData. Full serial is visible; exports can redact it.
- Current profiles use unbuffered/write-through filesystem I/O; controller caches may remain. Buffered QD1 measurements remain labeled in legacy history. Bounded sustained testing is implemented, but post-cache/NAND speed and proven SLC exhaustion are not established.
- Missing telemetry is retained as unavailable, unsupported or not implemented according to evidence. No universal hardware support or production-readiness claim is made.
- Follow the [installation and DPI procedure](installation.md) and [release checklist](release-checklist.md). See [current release validation](benchmark-ui-revamp-validation.md) for package evidence.
