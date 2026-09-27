# Compatibility assumptions

Evidence from this environment: .NET SDK 10.0.401, Microsoft.WindowsDesktop.App 10.0.12, Windows build 10.0.19045, win-x64 process architecture. Both Debug and Release builds pass. The existing net10.0-windows target is unchanged. Phase 5 adds the managed `Microsoft.Data.Sqlite` 10.0.12 package and its SQLitePCLRaw runtime dependencies; no native storage transport dependency was added. The project retains its original unspecified PlatformTarget; only x64 execution has been validated.

This is a proposed product validation matrix, not an OS lifecycle or universal hardware-support claim.

| Configuration | App execution | Detection | Detailed diagnostics | Hardware validation |
|---|---|---|---|---|
| Current Windows 10 build 19045, x64 host | Startup, WPF UI, SQLite history, reports and bounded filesystem benchmark verified; Phase 7 self-contained per-user installer install/launch/uninstall passed | SetupAPI + metadata queries verified without elevation | SATA implemented but physical validation NOT RUN; NVMe controller, health, error and assessment rules verified | One direct Intel NVMe disk; Phase 6 benchmark evidence reused without another real write run; installer is unsigned |
| Windows 11 x64 laptops/desktops/workstations | Proposed primary release validation target; unverified here | Implemented but unverified on this OS | SATA and NVMe implemented, unverified on this OS | Pending |
| Other Windows builds | Unverified; determine release policy separately | Unverified | Unverified | None |
| Windows x86 or ARM64 | Unverified; no support claim | Backend explicitly disabled outside x64 | Unverified | None |
| macOS / Linux | Not targeted by this native WPF application | N/A | N/A | N/A |

Application startup does not imply a drive can be detected or its diagnostics read. SATA/NVMe behind USB, RAID, bridges or vendor controllers may be unknown or limited. Discovery uses zero-desired-access metadata handles and ran with a non-elevated token here; denial remains possible with other stacks. No elevation request or elevated relaunch exists. Physical SATA health access has not been probed and is never inferred from protocol detection. SATA diagnostics require a read/write-access handle for the buffered ATA IOCTL. NVMe property queries succeeded through zero-desired-access handles on the available in-box stack, but other drivers may deny or omit queries. Only direct bus classifications are admitted; no USB SAT, RAID or generic protocol-command fallback exists. Namespace details require a trustworthy numeric NSID, which discovery does not currently supply. The Windows x64 transport boundary is tested; other architectures remain outside the validated boundary.

Phase 6 performance uses ordinary buffered filesystem files and should work independently of diagnostic protocol access, subject to directory permissions and free space. Only NTFS on the current Windows 10 system drive has actual validation. ReFS, FAT/exFAT, network/removable paths, encryption configurations, compression/deduplication, tiered/virtual storage and other Windows versions remain unverified. Filesystem results include cache effects and cannot be used as raw-device or manufacturer-maximum claims.

Phase 7 runtime layout was verified at a 1366×768 window and 1800×1000 desktop window on a 96-DPI host. Actual 125%, 150%, mixed-DPI/multiple-monitor movement and screen-reader checks were NOT RUN. Windows 11, ARM64, physical SATA, other NVMe controllers, multi-namespace devices and installer upgrade over an older signed release remain unverified.

DPI: the app uses WPF device-independent layout and system font rendering. Runtime verification is limited to one 1920×1080 monitor at 100% (work area 1920×1040). No DPI manifest override or additional architecture deployment target was introduced. Higher scaling and mixed-DPI behavior require the manual checks in phase-1-validation.md before release claims.

