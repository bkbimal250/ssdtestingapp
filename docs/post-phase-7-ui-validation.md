# Post-Phase-7 UI and data-presentation validation

Validated 2026-09-28. No diagnostic hardware command or benchmark write was run in this pass.

## Result

SATA retains Overview, SMART, Details and Raw Data. NVMe provides Overview, Details and Raw Data. Overview pages use compact identity, assessment, local observation time, friendly metrics and label/value usage rows. Exact counters, raw warning values, UTC timestamps, rule IDs, provenance, individual sensors and protocol payloads remain available under Details or Raw Data. The SATA table keeps row/column virtualization and an honest empty state.

Critical Warning 00h is presented as “No critical warning reported”; its hex value remains in Details. Percentage Used is explicitly consumed endurance. Sensor values distinguish **Not implemented** (zero field) from **Unavailable** (invalid/sentinel). Query outcomes separately display Available, Not queried, Unsupported and Access denied. “Coverage Complete” is qualified as complete only for the named diagnostic checklist. Missing Namespace Identify remains visible and does not invalidate independent SMART/Health checks.

## Accuracy findings

Saved Intel evidence was inspected without another device query. Windows discovery and Identify Controller both reported model `INTEL SSDPEKNW512G8` and firmware `004C`. The recorded discrepancy was the serial representation. The mapper now removes only defined NUL/space padding and normalizes whitespace; it names the exact fields that still differ and retains both sources. Synthetic checks prove padding-only identity differences disappear while a firmware mismatch remains visible.

LPA per-namespace capability did not establish that the prior SMART/Health request was namespace-specific because no numeric NSID was available. New results therefore state: selected physical device through Windows; controller-versus-namespace scope uncertain. Existing stored exports retain their original historical wording.

## Verification

- Release build: 0 warnings, 0 errors.
- Behavior suite: 230 passed.
- Full UI/applicable suite: 243 passed total, including the same 230 behavior checks.
- Actual WPF captures: `screenshots/post-phase7-nvme-1366x768.png` (1366×768 DIPs) and `screenshots/post-phase7-nvme-desktop.png` (1800×1000 DIPs). They use the honest unavailable state and contain no fabricated readings.
- Host scaling: 96 DPI. 125%, 150%, mixed-DPI and screen-reader checks: **NOT RUN**.
- Physical SATA, Windows 11, other NVMe controllers and multi-namespace hardware: unverified.

## Installer

Rebuilt self-contained x64 installer: `artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe`
SHA-256: `5C3FD8FE311A23B363EBA7BE1042021369BFFCE6462A8EABD8F262DDB5E1F2E2`
Status: unsigned. Silent install/uninstall of this build passed in a disposable directory; installed launch was not rerun because this pass prohibited new hardware commands. Release readiness remains **PARTIAL**.

## Installed-build verification — 2026-09-28

The existing installer was installed into `C:\Users\ADMIN\AppData\Local\PIXINIT-Verification-20260928`. The actual installed `PIXINIT.exe` was launched twice with only normal startup discovery. UI Automation selected tabs/expanded history; no diagnostic-read or benchmark action was invoked. No application code changed and no unrelated suites were rerun.

- Startup completed and automatically selected the single INTEL SSDPEKNW512G8 NVMe device. Live assessment/counters remained unavailable and diagnostics remained Not queried.
- NVMe Overview, Details and Raw Data navigation passed. Raw Data explicitly said Not queried; unavailable scope/counters were retained.
- SATA Overview, SMART, Details and Raw Data navigation passed. No SATA device/readings were inserted. Read SATA Diagnostics was disabled; SMART said No validated SMART attributes available. Minor existing wording: its footer says Awaiting discovery even after an empty SATA discovery; the selector and global completed status correctly report the actual state. No code change made for this cosmetic wording.
- History loaded 25 saved sessions. Selecting the 2026-09-27T13:41:28.4168936+00:00 record displayed Historical snapshot and its original timestamp/rules. Its original scope wording remains historical evidence, not a fresh diagnostic claim. Returning to live Overview still showed Assessment unavailable / Diagnostics not yet queried.
- Both launches closed normally through the window Close pattern and the processes exited within 10 seconds. No forced termination was used.
- Every installer-extracted payload file matched the existing self-contained publish directory by SHA-256, including PIXINIT.exe and PIXINIT.dll. EXE: B3C589F3D1F50C77C006C7C626D36F93FB183A34ACC9CBD004E2DFFC7E210FD3; DLL: 6EAE68A82DEC8A7A574B8431F3E52A441C608A27AFAC6F82E5E753E72EF44F32.
- Installer retained unchanged: SHA-256 5C3FD8FE311A23B363EBA7BE1042021369BFFCE6462A8EABD8F262DDB5E1F2E2. Unsigned. Installed launch is now verified on this host; clean-VM qualification remains pending.

### Actual full-window captures

These are screen captures of the installed process, including its native title bar, inspected after capture. The laptop view scrolls to lower metrics; no synthetic data or composed screenshots were used. An initially obstructed laptop capture was discarded and recaptured with the app visible.

- `C:\Users\ADMIN\Desktop\Bimal Works\Bimal Works\SSD\pixinit\docs\screenshots\installed-nvme-laptop-1366x768.png`
- `C:\Users\ADMIN\Desktop\Bimal Works\Bimal Works\SSD\pixinit\docs\screenshots\installed-nvme-desktop-1800x1000.png`
- `C:\Users\ADMIN\Desktop\Bimal Works\Bimal Works\SSD\pixinit\docs\screenshots\installed-sata-empty-1800x1000.png`
- `C:\Users\ADMIN\Desktop\Bimal Works\Bimal Works\SSD\pixinit\docs\screenshots\installed-history-1800x1000.png`

### Scaling and remaining qualification

GetDpiForWindow reported 96 DPI (100%). Actual 125%/150% and mixed-DPI verification: **NOT RUN**; an isolated scaled desktop was unavailable and the shared user's display scale was not changed. Screenshots do not prove DPI support.

Manual check: close PIXINIT; open Windows Settings > System > Display > Scale and layout; select 125%, relaunch the installed app and repeat both protocol tabs, secondary tabs, scrolling, keyboard traversal and history selection at laptop/desktop window sizes. Repeat at 150%; inspect clipping and reachable controls, then restore the original scale. Use startup discovery only; do not invoke diagnostics or benchmarks. Mixed-DPI requires two differently scaled displays and moving the window between them.

Physical SATA, Windows 11, other controllers, screen readers, clean-VM installation and signing remain unverified. Release qualification remains PARTIAL. Sustained-write testing remains NOT IMPLEMENTED.
