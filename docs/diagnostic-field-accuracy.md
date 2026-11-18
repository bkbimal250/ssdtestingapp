# Diagnostic field accuracy — 2026-11-09

NVMe SMART / Health Information Log defines Available Spare, Percentage Used,
Critical Warning, and Data Units Read/Written. Percentage Used estimates consumed
endurance, not filesystem occupancy. A data unit represents 1,000 × 512 bytes;
the existing NVMe conversion uses that factor and retains TB versus TiB units.

Source: https://learn.microsoft.com/en-us/windows/win32/api/nvme/ns-nvme-nvme_health_info_log

ATA SMART attribute IDs and units depend on model/firmware. Smartmontools' drive
database includes model-specific 241/242 mappings in GiB as well as LBA counters.
Do not assume 512-byte units or use filesystem occupancy to decide whether a
lifetime counter is valid. The supplied CrystalDiskInfo report identifies both
reported SATA disks with Silicon Motion layouts. Their F1/F2 counts use 32 MiB
units, confirmed by CrystalDiskInfo's `IsSsdSiliconMotion` and F1/F2 decoder.

Source: https://raw.githubusercontent.com/smartmontools/smartmontools/master/smartmontools/drivedb.h

Source: https://github.com/hiyohiyo/CrystalDiskInfo/blob/master/AtaSmart.cpp

The SATA overview omits the three NVMe fields, but shows host read/write totals
when the complete supported attribute prefix matches and that counter is valid.
Raw SMART attributes remain in the diagnostic result. Unsupported layouts remain
unavailable. Decoded totals also feed the footer and benchmark card. Manufacturer endurance
ratings remain separate from measured lifetime writes. SMART transport fallbacks
run when a valid attribute response is unavailable, not when an unverified
counter would round to 0.000 TB. The overview renders its actual field collection
so missing fields leave no indexed placeholders.

The two supplied SMART payloads (without serial/IDENTIFY data) are regression
fixtures. EVM F1=0x2431, F2=0x12BC gives 289.53125 GiB written and 149.875 GiB
read. The 256 GB disk F1=0x1B46, F2=0x1262 gives 218.1875 GiB written and
147.0625 GiB read. CrystalDiskInfo truncates these to integer values labelled
GB; PIXINIT labels binary GiB and decimal GB separately and retains precision.
No totals are hardcoded into production logic.

Verification covers actual payload parsing, malformed/missing/zero counters,
unsupported signatures, view-model consumers, existing NVMe conversion tests,
and the release/UI suite. Live reads on the user's machine still need confirmation.
