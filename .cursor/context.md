# PIXINIT context

WPF, .NET 8, `net8.0-windows`, MVVM, manual composition, shared `StorageOperationGate`. No SQLite, no WebView2, no new NuGet. English. No em dash in new text.

## Layout

- `pixinit/Core` devices, parsers, assessment
- `pixinit/Infrastructure/Windows/Discovery` SetupAPI, USB bridge, controller map
- `pixinit/Infrastructure/Windows/Nvme` storage-property NVMe and UASP admin pass-through
- `pixinit/Infrastructure/Windows/Sata` ATA and SAT
- `pixinit/ViewModels` and `pixinit/Views` overview
- Tests: `tests/pixinit.Tests/Program.cs` custom STA harness. Run the DLL. Do not use `dotnet test`.

## Buses

SATA 11, USB 7, RAID 8, NVMe 17. Direct NVMe health uses `IOCTL_STORAGE_QUERY_PROPERTY`. USB bridge `152D:0583` is NVMe over UASP, not SATA. JMicron is the bridge. Do not send SAT `0x85`, SAT `0xA1`, or vendor `0xDF` to that disk. Use the two-phase NVMe admin SCSI pass-through: CDB `A1 80` command phase, then `A1 82` data-in. Command buffer starts with ASCII `NVME`.

## Formulas

- NVMe host bytes = data units * 512 * 1000
- Crystal GB = round(bytes / 1024^3)
- TiB shown with that GB treated as decimal GB: GB * 1e9 / 1024^4
- Health percent = 100 - Percentage Used
- NVMe temperature Kelvin to C: Kelvin - 273
- SATA Good 100% when SMART status registers are missing if A9 current is 100, attribute 05 raw is 0, and C2 raw byte is under 75

## Build

Release, 0 warnings. Harness 469+ checks. Window 1280x800. Colors black `#000000`, white `#FFFFFF`, yellow `#FFB800`.
Harness: `dotnet artifacts/diagnostics-build/bin/pixinit.Tests/release/pixinit.Tests.dll --ui`
Build: `dotnet build pixinit.slnx -c Release --artifacts-path artifacts/diagnostics-build -warnaserror`
