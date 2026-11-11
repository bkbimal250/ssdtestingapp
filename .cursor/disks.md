# CrystalDiskInfo 9.9.4 ground truth

## Disk 0 Intel SSDPEKNW512G8 C:

- Serial PHNH214200KT512A from Identify Controller, not EUI64 `0000_0000_0100_0000_E4D2_5CDD_684C_5501`
- Health Good 94% = 100 - Percentage Used (ID 05 was 6)
- Data Units Read `00000345929B` = 26174 GB / 23.80 TiB
- Data Units Written `0000025A8B18` = 18829 GB / 17.12 TiB
- Composite temperature ID 02 `000000000142` = 322 K = 49 C
- Standard NVM Express 1.3
- Features S.M.A.R.T., TRIM, VolatileWriteCache
- Controller: Standard NVM Express Controller -> Intel

## Disk 1 512GB SSD D:

- NVMe over USB (UASP), not SATA. Model `512GB SSD`. JMicron is only the bridge, USB ID `152D:0583`
- Serial SES2326N351201099
- Firmware V1.0.2
- Interface UASP (NVM Express)
- Standard 1.4
- Do not send SAT `0x85` / `0xA1` / `0xDF` (ASC 24)
- SMART via NVMe admin pass-through
- Data Units Read `00000000214D` = 4 GB
- Data Units Written `000000003232` = 6 GB
- Power Cycles `00000000000F` = 15
- Power On `000000000001` = 1 h
- Temp sensor 1 `000000000135` = 36 C
- Temp sensor 2 `000000000130` = 31 C
- Available Spare 100
- Percentage Used 0
- Health Good 100%
- Controller: USB Attached SCSI (UAS) -> JMicron Tach

## Disk 2 2.5 SATA SSD 256GB E:

- Serial 2026011700468
- Firmware S1031C0
- Temp 40 C (C2 `0x28`)
- Power On 0 h (ID 09)
- Cycles 5 (ID 0C)
- Host reads F2 `0x61` = 3 GB
- Host writes F1 `0xC3` = 6 GB
- Enclosure Realtek RTL9201, VID `0BDA`, PID `9201`
- Health Good 100% when A9 = 100 (`0x64`), ID 05 reallocated = 0, and C2 < 75, even if SMART status registers are missing
- SMART IDs: 01, 05, 09, 0C, A0, A1, A3, A4, A5, A6, A7, A8, A9, AF, B0, B1, B2, B5, B6, C0, C2, C3, C4, C5, C6, C7, E8, F1, F2, F5
- Columns: ID, Cur, Wor, Thr, Raw
- Features S.M.A.R.T., NCQ, TRIM, DevSleep, GPL

## All disks

Controller map also includes Standard SATA AHCI and Microsoft Storage Spaces. Drive letters auto-follow C: / D: / E:.
