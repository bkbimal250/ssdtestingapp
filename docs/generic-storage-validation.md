# Generic storage diagnostics validation

- Changed StorageDevice, RaidDisplayFallback, ShellViewModel, MainWindow.xaml and discovery/UI harness tests.
- Removed EVM25 and generic SSD model hints as SATA protocol evidence. Explicit SATA/NVMe hints remain labeled heuristics and cannot authorize commands. Windows Storage media 4/3 is preserved as SSD/HDD when protocol remains unknown. RAID bus is 8; USB is 7 in Windows STORAGE_BUS_TYPE, not the values supplied in the prompt.
- Added Device overview inside Diagnostics for physical disks with observed capacity and unconfirmed protocol. It uses existing FirstPageView identity and DriveInfo storage usage, all-disk navigation and clear limited health disclosure. No invented temperatures, health percentages, genuineness, speeds or underlying protocol. Additional Devices retains unknown-capacity/virtual entries, is renamed and hidden at zero. Full serial remains in diagnostics; no export feature restored.
- Discovery remains SetupAPI, Win32_DiskDrive, MSFT_PhysicalDisk and partition cross-check with native extent mapping. Existing dedup retains distinct physical members even if RAID supplies repeated serials; blindly merging by serial/size would lose real disks. No brand/serial is hardcoded in application classification.
- Release build: zero warnings/errors; full applicable suite 400 unique checks including actual WPF UI harness. Four new checks cover generic SSD versus protocol, HDD media, virtual/unknown-capacity categorization and general overview routing. UI screenshots are harness fixtures, not real client RAID proof.
- Actual host: Windows 10, one Intel SSDPEKNW512G8 NVMe, PCIe 3.0 x4, discovery-only. No full physical benchmark or new physical write validation. Existing bounded disposable harness checks retained.
- Release verification log: artifacts/verification/universal-release.log. Installer resource update failed once with error 110; packaging-only retry succeeded (universal-installer-retry.log), security settings unchanged. Final size-check passed: publish 67,261,642 bytes, installer 62,317,734 bytes. Self-contained pure WPF .NET 8 single file, SQLite only; trimming remains disabled because WPF does not support it.
- Installer: artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe. SHA-256 B4566E077D163140D915A3B7E82278016E0EFB0B24CBC54D2C8383512AF3505E. Unsigned; install/uninstall NOT RUN.

## Pending coverage

| Configuration / capability | Status |
| --- | --- |
| Current direct NVMe discovery | Verified on Windows 10 host |
| Generic RAID/SSD/HDD routing and safety | Synthetic tests passed |
| Physical SATA, NVMe Gen4/Gen5, client Windows 11 | NOT RUN |
| Intel RST 17/18/19, AMD RAIDXpert2 RAID ON/OFF | NOT RUN; proprietary SMART transports not implemented |
| JMS583, RTL9210, ASM2362 USB NVMe / UASP / SAT SMART | NOT RUN; vendor transports and SAT not implemented |
| IOCTL_SCSI_GET_INQUIRY_DATA enumeration | Not implemented |
| Storage Spaces and eMMC diagnostics | Unverified; specialized health transport not implemented |
| Mixed DPI, 125/150 percent scaling | NOT RUN |

- PARTIAL, not CrystalDiskInfo parity or universal SSD/HDD support. A generic SCSI NVMe Identify CDB is not standardized for arbitrary bridges; vendor-specific commands need documented implementations and hardware checks. Missing telemetry stays unavailable; no health PASS from model hints. Benchmark examples are not measured values or brand-specific thresholds. General overview improves access to reported information while preserving strict native identity authorization.
