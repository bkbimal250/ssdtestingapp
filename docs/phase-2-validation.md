# Phase 2 validation

Phase 2 only is complete. The existing C# / WPF project, net10.0-windows target, resources and separate protocol layouts are retained. No SMART reads, ATA Identify, NVMe health log, pass-through, benchmark, raw write or firmware operation was implemented.

## Baseline and repairs

No applicable AGENTS.md or Git repository was present. Reviewed the architecture, Phase 1 validation, compatibility, roadmap, contracts, selection policy, protocol ViewModels, tests and build script.

First baseline verification failed because the running Release pixinit.exe (PID 19920) locked the output: MSB3027/MSB3021 after ten retries. Closed that instance normally, then reran. The Release build passed with zero warnings/errors. All 19 behavior checks and WPF startup passed, but UI verification failed at Program.cs line 110 because no TabControl existed: MainWindow.xaml had reverted to an empty starter window. Its exact pre-edit content is preserved in `baseline/MainWindow.phase2-before.xaml.txt`. Reconnected the Phase 1 shell to the existing SATA/NVMe views and styling; did not replace those screens with a demo. These were a resolved environment file lock and a repaired baseline UI defect, respectively.

## Discovery implementation

- SetupDiGetClassDevsW with PRESENT / DEVICEINTERFACE and GUID_DEVINTERFACE_DISK; SetupDiEnumDeviceInterfaces, bounded SetupDiGetDeviceInterfaceDetailW, and SetupDiGetDeviceInstanceIdW. No arbitrary PhysicalDrive numeric scan.
- Zero-desired-access CreateFileW metadata handles, shared read/write access; IOCTL_STORAGE_QUERY_PROPERTY for STORAGE_DEVICE_DESCRIPTOR and seek-penalty metadata.
- IOCTL_STORAGE_GET_DEVICE_NUMBER supplies the current disk number. IOCTL_DISK_GET_DRIVE_GEOMETRY_EX supplies physical DiskSize bytes; it does not use a volume size/free-space substitute.
- GetWindowsDirectoryW resolves the running Windows installation. GetVolumePathNameW and GetVolumeNameForVolumeMountPointW resolve its volume. IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS maps physical disk numbers. FindFirstVolumeW/FindNextVolumeW and GetVolumePathNamesForVolumeNameW add mounted-volume paths.
- No CIM/WMI fallback or extra package dependency. Windows API declarations and SafeHandles are in Infrastructure/Windows/Interop.

Reference documentation used: [SetupAPI enumeration](https://learn.microsoft.com/en-us/windows/win32/api/setupapi/nf-setupapi-setupdigetclassdevsw), [storage descriptor layout](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ns-winioctl-storage_device_descriptor), [disk geometry and size](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ns-winioctl-disk_geometry_ex), [volume disk extents](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddvol/ni-ntddvol-ioctl_volume_get_volume_disk_extents), [mount-point enumeration](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getvolumepathnamesforvolumenamew).

## Classification, identity and mapping

Only direct reported BusTypeSata / BusTypeNvme establishes SATA / NVMe. USB, ATA, RAID, SCSI, SAS, Spaces, virtual and unknown buses remain Other / Unidentified. No model-text, M.2 or speed heuristics. Seek penalty alone is insufficient evidence for media type: the current backend reports Unknown, even for the available NVMe device. This is an explicit conservative limitation, not a claim that it is an HDD.

The session key hashes normalized PnP instance + interface path, falling back to the path when instance lookup is unavailable. Matching serials/models or disk numbers never merge devices. Duplicate identical keys are removed; conflicting bus evidence for a duplicate withholds classification. Disk-number changes do not change the key. Reconnects may change or reuse path evidence: there is no persistent identity guarantee and no diagnostics are carried across a rescan.

Windows preference requires one distinct physical disk across all system-volume extents, exactly one corresponding enumerated record, and known direct protocol. Several extents on that one disk are allowed. Multiple disks, duplicate number matches, virtual/unknown protocol, access denial or missing mapping leave preference unset and expose a limitation. Disk 0 and C: are never assumed.

Initial selection prefers that unambiguous system disk, otherwise SATA before NVMe and ordinal session-key order. Independent selections survive successful rescans; removed devices clear only after successful enumeration. Manual tab choices, including empty tabs, persist. Choices made during a scan win over completion. Unknown-only results expand Other / Unidentified. Empty successful enumeration is distinct from failure/cancellation, which retains visibly stale information.

## Cancellation and resource boundaries

One coordinator worker is allowed at a time; all native operations are serial and off the dispatcher. SetupAPI, CreateFile, DeviceIoControl and volume APIs are synchronous and have no application-enforced hard native-call timeout. A 30-second overall acceptance budget requests cooperative cancellation. Cancellation checks run between disk queries/devices/volumes, and the coordinator checks again before accepting any result. A native call that is already blocked must return before the worker can finish; new scans stay disabled throughout. No abandoned-worker replacement is started. Generation checks reject obsolete results. Window close stops result acceptance and does not synchronously wait for native calls; normal process termination lets Windows reclaim outstanding resources.

SafeHandle owns file/device-set/volume-search handles. Unmanaged interface buffers are freed in finally; managed IOCTL buffers and their handles remain alive until each synchronous call finishes. Returned byte counts, sizes, offsets, terminators, raw-property lengths and extent counts are checked, with a maximum allocation of 1 MiB. The x64 SetupAPI structure sizes are verified by tests; other process architectures have discovery disabled.

Optional property errors produce partial records and friendly limitations. A failed/incomplete disk enumeration retains the previous whole inventory. Log file: `%LOCALAPPDATA%\PIXINIT\Logs\discovery.log`, rotated at approximately 2 MB to `.previous`. It records query names, hashed device prefixes, HRESULT and Win32 codes, excluding raw paths/full serials. Log failure does not block discovery.

## Actual hardware observations

Final recorded run: Windows 10 build 19045, x64 process, **non-elevated token**. Only the attached machine's disk was inspected; no device was disconnected or storage configuration changed.

| Field | Observed result |
|---|---|
| Enumerated disks | 1 |
| Current disk number | 0 |
| Model | INTEL SSDPEKNW512G8 |
| Reported bus / classification | Bus 17, NVMe / NVMe |
| Physical capacity | 512,110,190,592 bytes; displayed 512.1 decimal GB |
| Firmware | 004C |
| Serial | Present; full value excluded from report and screenshots |
| Media | Unknown; Windows reports no seek penalty |
| Removable | False |
| Metadata access | Available |
| Mount entries | 2 |
| Windows mapping | Unambiguous to this disk |
| Automatic selected tab | NVMe |
| Initial discovery wait | 84 ms in final harness run; includes observation/polling overhead |
| Rescan | 13 ms; selections preserved; dispatcher timer processed work |

`phase-2-hardware-observations.txt` contains the sanitized machine observations. The standalone production Release exe was also launched separately, reached input idle with title PIXINIT · Storage diagnostics, logged one NVMe disk on its automatic discovery, and closed normally. The UI/hardware harness uses the actual App startup composition and MainWindow, not a substituted discovery provider.

## Executed verification

```powershell
.\build\verify.ps1 -Ui -Hardware
```

Result: Release build **zero warnings / zero errors**; UI suite **60/60 passed**; hardware suite **57/57 passed**. Both suites include the same 52 behavior checks, so this is **65 distinct checks**, not 117 independent tests. The executable harness is not VSTest; `dotnet test` does not execute these checks. Raw final output is in `phase-2-verification-output.txt`.

Coverage includes SATA-only, NVMe-only, mixed/multiple devices, Unknown media without SSD inference, ambiguous buses, virtual devices, missing/duplicate serials, duplicate records and conflicts, bad sizes/offsets/terminators, invalid capacity, denied partial records, one failed device alongside a successful one, empty versus failed enumeration, cancellation/late results, blocked-worker exclusion, close, changed disk numbers/removals, ambiguous extents, independent selection, choices during scanning, later empty-tab inspection, stale inventory and one-time automatic discovery. UI checks retain Phase 1 focus, long-string, resize and 2,000-row virtualization coverage. These fixture cases are not physically tested hardware configurations.

## Screenshots

Actual shown WPF client visuals captured using RenderTargetBitmap at 96 DPI, excluding OS chrome. Serial is masked by the application itself. Technical paths/instance IDs remain in expandable details and are not exposed in these captures.

- `screenshots/phase2-laptop.png`: 1280×700 DIP window, 1264×661 client bitmap, real NVMe discovery.
- `screenshots/phase2-desktop.png`: 1800×1000 DIP window, 1784×961 client bitmap, real NVMe discovery.

Captures were visually reviewed. Host monitor: 1920×1080 at 100%, work area 1920×1040. Phase 1 higher-scaling, mixed-DPI, full keyboard/screen-reader and additional display configurations remain unverified.

## Significant changed files

- `pixinit/Infrastructure/Windows/Discovery/WindowsDiskDiscovery.cs`, `DescriptorParser.cs`: discovery, classification, identity, mapping, input validation.
- `pixinit/Infrastructure/Windows/Interop/StorageNative.cs`: native signatures and SafeHandles.
- `pixinit/Infrastructure/Logging/DiscoveryLog.cs`: bounded local technical logs.
- `pixinit/Core/Devices/StorageDevice.cs`: basic discovery metadata and explicit media/bus separation.
- `pixinit/Application/Discovery/DiscoveryCoordinator.cs`, `Application/Selection/DeviceSelection.cs`: worker exclusion and preserved manual tab intent.
- `pixinit/ViewModels/Shell/ShellViewModel.cs`, `ViewModels/Shared/DeviceViewModel.cs`: scan lifecycle, generations, stale status, masked identity and details.
- `pixinit/App.xaml.cs`, `MainWindow.xaml(.cs)`, protocol views: production wiring, automatic scan, restored shell and real metadata display.
- `tests/pixinit.Tests/DiscoveryTests.cs`, `HardwareTests.cs`, `Program.cs`, `build/verify.ps1`: extended executable validation and real hardware capture.
- README, architecture, compatibility, roadmap and this report.

## Remaining limitations and exact manual steps

There are no remaining local build/runtime blockers. No known failing implementation check remains. The following coverage/capability boundaries remain:

1. From the repository root, run `dotnet run --project pixinit -c Release --no-build`. Confirm one automatic scan, masked serial, real capacity, and unavailable health fields. Click Scan Drives to rescan and Cancel during a scan. If Windows stalls, the UI must explain that it is waiting and must not start another worker. Close normally if needed; there is no hard native abort guarantee.
2. Run `build/verify.ps1 -Ui -Hardware` to reproduce both suites. Hardware expectations are observational, not hardcoded to the INTEL disk. Inspect `%LOCALAPPDATA%\PIXINIT\Logs\discovery.log` for query failures. Do not elevate as a default workaround; verify denied records and limitations first.
3. With separately available non-system test devices, validate SATA HDD/SSD, USB bridges, RAID, Spaces, virtual disks and multiple direct disks. These were fixture-tested only here. Do not disconnect the system drive. Uncertain media remains Unknown until a verified source is added in an authorized scope.
4. Perform the exact display, Narrator, keyboard, maximize/restore and mixed-DPI procedures in `phase-1-validation.md`. No additional monitor/scaling claims are made in Phase 2.
5. Devices that cannot expose metadata through the read-only Windows stack remain partial. Raw protocol identities/health, persistent device correlation, volume size/free space and automatic hot-plug monitoring are outside this phase.

**Phase 3 readiness: READY for implementation.** Real discovery and selection are connected, bounded-worker behavior and failure cases are tested, and the SATA provider contract/layout is preserved. Physical SATA validation will require a SATA device; none was available here. No Phase 3 work has been started.
