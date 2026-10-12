# Multi-disk discovery validation

- SetupAPI already enumerated every present disk interface; there was no fixed single-NVMe loop. Client root cause remains unconfirmed without its detection log.
- Added independent Win32_DiskDrive enumeration through Windows COM WMI, without a new package. Missing disk numbers are queried through their validated PhysicalDrive paths. WMI model is a fallback label only; SATA/NVMe classification still requires Windows storage bus evidence. SetupAPI failure now attempts the independent inventory. Failed opens remain visible as limited/access-denied devices.
- Existing read-only storage descriptor discovery does not depend on NVMe Identify success. Existing SATA provider uses ATA Identify and SMART IOCTL commands separately. No new health commands run during discovery. USB/RAID/virtual disks remain Other when underlying protocol is not proven; controller-specific pass-through remains unsupported.
- Added top all-physical-disk selector; confirmed protocol selection switches to SATA/NVMe, unknown selection to Other. Completion status includes actual disk and protocol counts.
- Existing manifest already requests requireAdministrator. Added defensive non-admin startup message. Windows handles UAC before startup; denying UAC prevents launch. Elevation is this application deployment policy, not proof Windows 11 always requires elevation for metadata enumeration. Installer already launches as current elevated user.
- Detection events/errors also written to %TEMP%/PIXINIT_detect.log, rotated at 2 MB. Native Win32 errors retain numeric codes; COM failures retain HRESULT. No serial/interface/instance identifiers in this log. LocalAppData discovery log retained.

## Verification

- Release build: 0 warnings, 0 errors. Log: artifacts/verification/multi-disk-build.log.
- Full applicable suite: 382 unique checks, including three new mixed 1 NVMe + 2 SATA inventory/selector checks. Log: artifacts/verification/multi-disk-tests.log.
- Read-only host metadata discovery: Windows 10 Pro, one Intel NVMe, actual PCIe 3.0 x4. No benchmark or diagnostic health commands. Log: artifacts/verification/multi-disk-host-discovery.log.
- Physical Windows 11 with 1 NVMe + 2 SATA: NOT RUN. Client controller, RAID mode, access errors and physical SATA compatibility remain unverified. Simulated inventory tests do not establish hardware compatibility.
- Release packaging subsequently completed: self-contained Windows x64 publish and Inno Setup installer, 0 warnings/errors, 382 checks. Log: artifacts/verification/multi-disk-release.log.
- Installer: artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe; 47,702,974 bytes (47.70 decimal MB), below 100 MB. SHA-256: 349F6C8BFEA3D81C689A3304350ADD3021E7CA25950C72659CCFDE890B7045F6. Authenticode: NotSigned. Installation/UAC lifecycle NOT RUN.
- Published application folder: 151,350,147 bytes (151.35 decimal MB), above 100 MB. No under-100-MB installed-folder claim. WPF self-contained runtime dependencies retained. No Electron, fixed-package.json, locales/SwiftShader stripping or npm scripts.
- Added build/size-check.ps1; exits 1 when either published folder or installer exceeds 100,000,000 bytes. The current check reports folder OVER LIMIT, installer PASS.
- Reproduce in PowerShell: .\build\release.ps1 then .\build\size-check.ps1. Commands require the project root. No package was sent to the client.

## Client check

- Launch elevated, select Scan Drives, inspect the all-disk selector and Other tab.
- Compare displayed inventory with Get-CimInstance Win32_DiskDrive in PowerShell.
- Send %TEMP%/PIXINIT_detect.log after a missing-disk scan. Do not benchmark for this verification.
