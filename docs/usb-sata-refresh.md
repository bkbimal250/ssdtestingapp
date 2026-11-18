# USB SATA refresh — 2026-11-11

The JMicron screenshot shows a SATA-classified disk on Windows bus type USB (7).
Previously CanQuerySata excluded USB, disabling automatic reads and Refresh.
USB SATA now uses read-only SCSI ATA PASS-THROUGH, not native ATA passthrough.
The allowlist remains IDENTIFY, SMART READ DATA, SMART READ THRESHOLDS, and SMART
RETURN STATUS. SAT-16 is tried first; an unsupported result permits SAT-12.
There are no vendor-private requests, writes, SMART enable commands, or self-tests.

Every request revalidates the selected interface, bus, disk number, bridge model,
serial, firmware and capacity. Windows can expose an enclosure serial distinct
from the ATA drive serial, so this difference is disclosed instead of rejecting
all subsequent reads. ATA identity must still parse successfully and report SMART
supported/enabled. Short transfers, invalid sense descriptors, ATA errors and
missing SMART status registers never become successful health evidence.

An unsupported bridge leaves Refresh available for retry and explains the failed
read. Actual support depends on the enclosure firmware; hardware verification
on the reported JMicron device remains necessary. Proprietary JMicron command
variants are not implemented.

References:
- https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddscsi/ni-ntddscsi-ioctl_scsi_pass_through_direct
- https://github.com/smartmontools/smartmontools/blob/master/smartmontools/scsiata.cpp

Tests cover both CDB layouts, exact SMART low-byte register placement, response
validation, bridge serial differences, changed-device rejection, repeated reads,
and retry after unsupported responses. The release includes the separately tested
Silicon Motion host-counter decoder and actual SMART payload fixtures.
