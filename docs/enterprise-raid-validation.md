# Enterprise RAID display fallback validation

## Changes

- Deduplication uses serial + capacity + physical disk number when all exist, otherwise session interface identity. Serial alone is not unique on every RAID controller. Same-number/serial/capacity duplicates merge, mount points are united and stronger validated evidence preferred. Conflicting buses withhold classification. Separate physical numbers remain separate even with identical controller serials. Merge events logged without serials.
- Query MSFT_PhysicalDisk through existing Windows COM WMI, no package added. Match evidence by capacity and serial or device ID; require one matching record. Provider bus 17/11 is NVMe/SATA evidence for display; bus 8 remains RAID. Bus 7 is USB in the Windows storage bus enum, not RAID.
- For failed RAID Identify and no access denial, use MSFT_PhysicalDisk evidence, then no-seek-penalty plus explicit NVMe/SSD/EVM25 model indication. This is a display heuristic, not protocol establishment. All such results retain Limited access and reported RAID bus. Matching evidence can report SSD; model/no-seek inference displays SSD (inferred) without turning it into verified media.
- Provisional disks enter existing SATA/NVMe navigation. UI badges show via RAID, Heuristic detection, Limited access and Health not assessed. Serial ending four characters in navigation only; full serial remains in detail/overview. Gray health marker means no live assessment, not healthy. Protocol warnings visible in both view headers. History captures heuristic/inferred flags.
- Verified native transports remain gated by CanQuerySata/CanQueryNvme. A guessed model name or WMI record cannot authorize ATA/NVMe requests. No fabricated SMART readings, PASS assessment, temperature or endurance.
- User-facing fallback explanation points to TEMP detection log. Log records probe native errors, raw descriptor headers and bounded 128-byte Identify previews with serial bytes zeroed. Successful full raw identity remains retained in history; export redaction safeguards preserved.
- No proprietary Intel RST miniport or legacy SMART_RCV_DRIVE_DATA path implemented. Such commands require verified controller routing/identity, not guessed physical disk indices. Model-derived protocol labels do not justify arbitrary driver commands.

## Verification

- build/release.ps1: zero warnings/errors, 391 unique checks passed (388 previous + 3 display/dedup regressions). Existing malformed/invalid identity tests plus display-fallback tests preserve transport rejection.
- Tests cover provisional RAID NVMe with MSFT SSD evidence, provisional SATA model inference, distinct physical numbers sharing a serial, duplicate physical records and existing direct/probed RAID guards. WPF viewport checks still pass at laptop, desktop and minimum sizes.
- Publish contains single self-contained net8.0-windows WPF PIXINIT.exe: 67,263,494 bytes. Installer: 62,319,741 bytes. Both below decimal 100 MB. No WebView2; no WPF trimming.
- Installer artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe; SHA-256 E853A6C52F99441FBA0708649DCBC2E054C410EB53606552DC43BC0F075E7C8E. Unsigned, not installed in this pass.
- Verification log: artifacts/verification/enterprise-raid-release.log. Updated summary. No full physical benchmark, extra physical SMART reads or BIOS changes.

## Limits and client check

- Actual Windows 11 client three-disk RAID NOT RUN. Heuristic labels cannot prove protocol, SSD type or member-disk routing. Enumeration depends on the driver presenting those disks; no supports-all-controllers claim.
- Existing verified EVM SATA path remains enabled. Unverified disks can use the filesystem benchmark only when actual volume mapping, free-space checks, operation gate and explicit write consent pass; no guarantee that all three client targets work and no physical benchmark executed here.
- SMART for the two heuristic entries remains unavailable until a valid attributable response or documented controller-specific implementation exists. Displaying them in Diagnostics does not make their SMART accessible.
- Isolated install/upgrade/uninstall, mixed DPI and 125/150 percent scaling NOT RUN. Old multi-file installations can retain legacy files on upgrade.
- Client: install this build, Scan Drives, verify unique dropdown entries and badges. If routing is wrong or metrics unavailable, provide %TEMP%/PIXINIT_detect.log and controller/driver identity. Do not run a benchmark just to verify discovery.
- Release readiness PARTIAL.
