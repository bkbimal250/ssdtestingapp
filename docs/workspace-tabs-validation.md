# Workspace tabs — 2026-10-07

Replaced the three bottom expanders with dedicated Benchmark, Diagnostic History and Other Devices workspace tabs beside Diagnostics. SATA/NVMe and protocol-specific secondary navigation remain inside Diagnostics. Only the selected workspace is shown; changing tabs does not trigger diagnostic reads or benchmarks.

Benchmark retains its prominent buffered/QD1 disclosure, consent controls, compact result cards and scrollable settings/history/export content. History uses session cards and a bounded historical-detail card with its original observation time. Other devices use compact identity cards with explicit protocol uncertainty. Empty sources remain empty; no readings were fabricated.

Release/UI verification uses the existing isolated harness, not physical diagnostic or benchmark commands. Runtime captures at 1366x768 and 1800x1000 are fixture-based WPF captures: phase7-responsive-1366x768.png, phase7-responsive-desktop.png, workspace-history-desktop.png, workspace-other-desktop.png under docs/screenshots. New checks confirm workspace switching hides unrelated controls and the Other tab retains scope wording. DPI tests beyond 96 DPI and installed UAC launch remain NOT RUN.

The installer also retains the error-740 correction: its postinstall action uses runascurrentuser to inherit the elevated installer token. Current installer SHA-256 is in artifacts/installer/SHA256SUMS.txt. No GitHub publication performed.

Final verification: Release build 0 warnings/errors; 248 checks passed (233 behavior plus 15 UI). Installer compiled successfully. SHA-256: 4D1A0C523A16B215EACD54C5F65EBD9D947B610CE47E50DC6E853AC11A1B6E31. Interactive installed launch is not verified in this pass.
