# Compatibility assumptions

Evidence from this environment: .NET SDK 10.0.401, Microsoft.WindowsDesktop.App 10.0.12, Windows build 10.0.19045, win-x64 process architecture. Both Debug and Release builds pass. The existing net10.0-windows target is unchanged. Phase 5 adds the managed `Microsoft.Data.Sqlite` 10.0.12 package and its SQLitePCLRaw runtime dependencies; no native storage transport dependency was added. The project retains its original unspecified PlatformTarget; only x64 execution has been validated.

This is a proposed product validation matrix, not an OS lifecycle or universal hardware-support claim.

| Configuration | App execution | Detection | Detailed diagnostics | Hardware validation |
|---|---|---|---|---|
| Current Windows 10 build 19045, x64 host | Startup, WPF UI, SQLite history and TXT/JSON export verified | SetupAPI + metadata queries verified without elevation | SATA implemented but physical validation NOT RUN; NVMe controller, health, error and assessment rules verified | One direct INTEL NVMe disk; two non-elevated reads, history restart/reopen, comparison/export and zero ATA dispatches verified in Phase 5 |
| Windows 11 x64 laptops/desktops/workstations | Proposed primary release validation target; unverified here | Implemented but unverified on this OS | SATA and NVMe implemented, unverified on this OS | Pending |
| Other Windows builds | Unverified; determine release policy separately | Unverified | Unverified | None |
| Windows x86 or ARM64 | Unverified; no support claim | Backend explicitly disabled outside x64 | Unverified | None |
| macOS / Linux | Not targeted by this native WPF application | N/A | N/A | N/A |

Application startup does not imply a drive can be detected or its diagnostics read. SATA/NVMe behind USB, RAID, bridges or vendor controllers may be unknown or limited. Discovery uses zero-desired-access metadata handles and ran with a non-elevated token here; denial remains possible with other stacks. No elevation request or elevated relaunch exists. Physical SATA health access has not been probed and is never inferred from protocol detection. SATA diagnostics require a read/write-access handle for the buffered ATA IOCTL. NVMe property queries succeeded through zero-desired-access handles on the available in-box stack, but other drivers may deny or omit queries. Only direct bus classifications are admitted; no USB SAT, RAID or generic protocol-command fallback exists. Namespace details require a trustworthy numeric NSID, which discovery does not currently supply. The Windows x64 transport boundary is tested; other architectures remain outside the validated boundary.

DPI: the app uses WPF device-independent layout and system font rendering. Runtime verification is limited to one 1920×1080 monitor at 100% (work area 1920×1040). No DPI manifest override or additional architecture deployment target was introduced. Higher scaling and mixed-DPI behavior require the manual checks in phase-1-validation.md before release claims.

