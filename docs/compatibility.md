# Current compatibility and validation matrix

Updated 2026-10-08. This describes the current release candidate. Earlier phase reports contain historical evidence, not guarantees for this build.

| Configuration or behavior | Current evidence |
| --- | --- |
| Windows x64 Release build and self-contained publish | Verified on the development host |
| Automated behavior and WPF harness | 310 checks; bounded disposable write tests only |
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
- Benchmark results are buffered filesystem QD1 measurements with caching effects. Bounded sustained testing is implemented, but post-cache/NAND speed and proven SLC exhaustion are not established.
- Missing telemetry is retained as unavailable, unsupported or not implemented according to evidence. No universal hardware support or production-readiness claim is made.
- Follow the [installation and DPI procedure](installation.md) and [release checklist](release-checklist.md). See [current release validation](release-2026-10-08.md) for package evidence.