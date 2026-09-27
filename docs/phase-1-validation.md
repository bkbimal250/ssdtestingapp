# Phase 1 validation

## Baseline

`dotnet build pixinit.slnx` passed with zero warnings/errors. `dotnet test pixinit.slnx --no-build` exited successfully but the baseline contained no test projects: zero tests executed. The app was an empty WPF starter. No hardware functionality existed to preserve. No Git repository or applicable AGENTS.md was present.

## Final executed checks

- `dotnet build pixinit.slnx`: passed, zero warnings/errors.
- `dotnet build pixinit.slnx -c Release`: passed, zero warnings/errors.
- `dotnet run --project tests/pixinit.Tests -c Release --no-build -- --ui`: all **27 checks passed**.
- `dotnet test pixinit.slnx -c Release --no-build`: this solution uses an executable harness, so this command does not run the 27 checks. Use the command above or build/verify.ps1.
- Launched the production executable separately, waited for input idle, verified title `PIXINIT · Storage diagnostics`, and closed it successfully.

The 19 behavior checks cover single/both protocol discovery, ambiguous system mapping, deterministic fallback, independent retained selections, disconnects, changes during discovery, unknown protocol, zero versus missing data, invalid metrics, scan exclusion, completion, clearing stale readings, cancellation, errors and production empty state. Eight additional runtime checks cover window startup, persistent tabs, actual NVMe content switching, focus/traversal, minimum sizing, long identity layout and bounded DataGrid virtualization with 2,000 test rows (fewer than 100 realized rows).

## Captures and visual review

Captures are generated from the actual shown WPF window's client visual using RenderTargetBitmap at 96 DPI. They exclude OS window chrome; they are runtime captures, not image mockups or full-desktop screenshots. Normal captures use production unavailable discovery. `test-only-long-identity.png` is explicitly a test fixture and must never be presented as a discovered device.

| Capture | Window size in DIPs | Client bitmap |
|---|---|---|
| screenshots/sata-laptop.png | 1280×700 | 1264×661 |
| screenshots/nvme-laptop.png | 1280×700 | 1264×661 |
| screenshots/sata-desktop.png | 1800×1000 | 1784×961 |
| screenshots/nvme-desktop.png | 1800×1000 | 1784×961 |
| screenshots/sata-minimum.png | 760×640 | 744×601 |
| screenshots/test-only-long-identity.png | 760×640 | 744×601 |

Visual review found and fixed resource inheritance/transparent capture background and a cramped SATA viewport. SATA now has a section scroller with an explicitly bounded table viewport; row virtualization was verified in the running app. Details move below the table below 1050 DIPs. NVMe metrics change from four to two columns below 1000 DIPs; detail sections stack below 850. Long identity strings truncate with full-value tooltips. Native keyboard focus and control behavior are retained. No whole-window Viewbox is used.

The shell/header/actions/status remain outside content scrollers. Initial dimensions are clamped to the primary work area. No saved window coordinates or dialogs exist yet. There are no local build or WPF execution blockers.

## Remaining manual checks

These are unverified configurations, not passing results inferred from XAML:

1. From the repository root, run `dotnet run --project pixinit -c Release --no-build`.
2. In Windows Settings → System → Display, verify 1366×768 at 100%; 1920×1080 at 100%, 125%, 150%; 1920×1200 with scaling; 2560×1440; and 3840×2160 with scaling. Restart the application after each setting change and record resolution, scaling and screenshots. Only the single 1920×1080/100% host and the window sizes above ran here.
3. Resize down to minimum; scroll each protocol view and the SATA details pane, expand Other / Unidentified, and confirm primary actions and status remain reachable. At short heights, some details intentionally require scrolling.
4. Use Alt+S / Alt+N for protocol tabs, Tab/Shift+Tab and arrow keys. Inspect visible focus, disabled-action explanation and accessible names using Narrator. Automated checks exercised focus/traversal, not full screen-reader or physical-key coverage.
5. For long strings and populated-table behavior run `dotnet run --project tests/pixinit.Tests -c Release --no-build -- --ui`. The test executable isolates fixtures from production. Real populated selectors require Phase 2; selection logic is already tested using controlled discovery.
6. On mixed-DPI monitors, drag the window between displays, maximize/restore on each, disconnect the secondary display and verify accessibility of the title bar and controls. No second monitor was available for this validation.
7. After real providers exist, validate actual disks, controller combinations and cancellation under slow native I/O. No hardware detection, health accuracy or device compatibility was tested in Phase 1.

**Phase 2 readiness: READY.** The foundation builds and runs, selection policy is tested, and provider contracts are prepared. No hardware code, firmware operations, raw writes or benchmarks were added. Display/hardware release certification remains pending.
