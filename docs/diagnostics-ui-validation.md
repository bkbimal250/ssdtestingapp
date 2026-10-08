# Diagnostics UI refresh — 2026-10-07

Both SATA and NVMe now provide Overview, SMART, Details and Raw Data. Existing protocol-specific models/providers remain separate. No additional hardware command or real benchmark was run.

Overview uses smaller identity text, compact metric cards, restrained accent marks and assessment panels. Assessment backgrounds use the existing typed state: neutral unknown, green NoIssuesDetected, amber Attention/Warning, red Critical. Visible status wording accompanies every color; green is checklist evidence, not a remaining-life guarantee. Neutral body text does not inherit the active tab color.

NVMe SMART is a native health-field table, not an ATA attribute table. It includes five health fields and all ten exact counters, with units and the zero-data-unit caveat. BigInteger text is lossless; selecting another device clears the rows. SATA keeps its virtualized attribute table. Details use independently expandable cards; raw responses stay read-only/copyable with all source data preserved. Full serials, actual warnings, identity discrepancies, uncertainty and availability are retained.

Validation: Release build using --artifacts-path artifacts/diagnostics-build succeeded with zero warnings/errors. This avoids overwriting the user's running PIXINIT build. Applicable suite passed 252 checks (235 behavior, 17 UI). One preceding run hit the pre-existing asynchronous cleanup race in Phase6Tests.cs:79: the owned directory disappeared before redirect.owner.json creation. The unmodified suite passed on rerun; no production cleanup logic was changed.

WPF runtime screenshots inspected at laptop and desktop sizes: docs/screenshots/post-phase7-nvme-1366x768.png, sata-desktop.png, diagnostics-nvme-smart-desktop.png, diagnostics-nvme-details-desktop.png. These are honest unavailable-state fixture windows, not populated hardware evidence. 125%/150%, mixed DPI, screen readers, physical SATA and installed UAC launch remain NOT RUN.

Self-contained x64 publication and development-file exclusion check precede Inno compilation. Existing error-740 correction remains in [Run]. Package remains unsigned; the final SHA-256 is recorded in artifacts/installer/SHA256SUMS.txt. Changes are local; no GitHub push in this pass.
