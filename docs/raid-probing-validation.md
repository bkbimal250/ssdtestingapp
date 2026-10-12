# RAID protocol probing validation

## Implemented

- Client screenshots establish enumeration of three disks, all reported through a RAID bus. Names such as NVMe or SATA are not protocol evidence. No-seek-penalty and TRIM alone are not SSD proof.
- Added StorageQueryService for RAID bus type 8 only. Queries device-scoped IOCTL_STORAGE_QUERY_PROPERTY Identify for NVMe, then ATA; falls back to existing allowlisted data-in IOCTL_ATA_PASS_THROUGH Identify. No disk writes, guessed namespace or arbitrary SCSI/NVMe command interface added.
- Only bounded, validated descriptor/payload responses can classify. Identify serial must match the selected discovery serial. ATA also needs SATA generation capability bits; SSD media requires rotation word 217 equal to one. NVMe media stays unconfirmed independently of protocol.
- RAID reported bus/native bus remain unchanged. New verified-protocol/evidence fields allow existing guarded SATA/NVMe diagnostics after repeated identity revalidation. RAID NVMe requests use device property rather than adapter attribution. Driver scope uncertainty remains explicit. Existing shared gate and cancellation preserved.
- Identify payload retained in device/history. Redacted exports omit that raw identity payload. Protocol discrepancies remain available; model/serial/firmware discovery data are not overwritten.
- ATA elevated handle uses shared read/write access for a data-in command and revalidates physical disk number before Identify. Access failures log native codes and show an administrator/driver restriction message. Administrator privileges are not represented as a guarantee of SMART availability. Successful metadata with unconfirmed media no longer automatically means limited access.
- Detection log remains %TEMP%/PIXINIT_detect.log. Probe names, outcomes, disk numbers and native error codes recorded without serials. Existing admin manifest, top selector, counts, pure WPF and compressed self-contained packaging retained.

## Verification

- build/release.ps1 completed: Release zero warnings/errors, 388 unique checks passed (382 previous + 6 RAID checks). Tests cover attributed NVMe, ATA SSD rotation, simulated 1 NVMe + 2 SATA inventory, error 5, serial mismatch and malformed response. Fixtures do not prove client driver compatibility.
- Existing direct-path transport/guard tests still pass. Actual read-only Windows 10 metadata discovery finds Intel SSDPEKNW512G8 with PCIe 3.0 x4. No physical write benchmark or health query run. BIOS/controller mode not changed.
- Logs: artifacts/verification/raid-release.log and raid-host-discovery.log.
- Single-file publish 67,260,144 bytes; installer 62,316,273 bytes. Both below 100,000,000 bytes. Trimming remains disabled because WPF does not support trimming.
- Installer: artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe. SHA-256 3C3F664F8A2947AC7AA25540F4D4A14B654DA2876042A1979A8F10CDC9496B18. Unsigned; not installed for this task.

## Unverified and remaining limitations

- Windows 11 client hardware and RAID-enabled/disabled BIOS comparison NOT RUN. This machine has no matching three-disk RAID setup; do not change a boot controller's BIOS mode merely to test the app.
- No Intel RST vendor-specific miniport or proprietary NVMe-over-SCSI implementation. SCSI opcode 0xA0 is REPORT LUNS, not NVMe Identify; no guessed CDB sent. Drivers rejecting standard property/ATA pass-through remain unsupported.
- Missing/mismatched discovery serial, absent SATA capability evidence or inaccessible Identify keeps the disk in Other with the actual reason. No guarantee all three client disks will move into Diagnostics until real attributable responses are obtained.
- Some RAID drivers expose synthetic logical-volume identities rather than member disks. An adapter response alone cannot establish individual member identity.
- After installing this package, scan and send %TEMP%/PIXINIT_detect.log if devices remain unidentified. Logs determine whether a documented controller-specific path is needed. Do not benchmark for discovery verification.
- Installer lifecycle, isolated runtime-free machine, 125/150 percent scaling and mixed DPI remain pending. Release readiness PARTIAL.
