# Phase 7 — release hardening and installer validation

Validated 2026-09-27. No publish or distribution occurred. Release readiness is **PARTIAL**.

## Baseline and scope

The Phase 6 baseline passed a Release build with zero warnings/errors, 217 behavior checks, 225 UI checks and 253 checks in the last consent-approved Intel hardware run. Phase 7 did not run another physical write benchmark because no new explicit write consent was obtained. The Phase 6 Intel measurements remain evidence only for that bounded run.

Phase 7 preserves `net10.0-windows`, separate SATA/NVMe diagnostics, SQLite history, explicit diagnostic actions, benchmark byte limits, cancellation and stale-result protections. No raw-device write, sustained-write mode, repair, firmware or optimization feature was added.

## Benchmark accuracy

The UI section boundary, detailed result, TXT and JSON now identify every result as **“Buffered filesystem benchmark — QD1; caching affects results.”** Configuration/history/export persist target path, volume root, filesystem, operation selection, file/block sizes, iterations, random count, QD1, buffering, `FlushAsync` behavior, lack of write-through/durable `Flush(true)`, measured durations and the exclusion of preparation/warm-up/cleanup from primary throughput.

Write budgets are labeled application data bytes and are not physical NAND-write estimates. No uncached-device speed, durable-write latency or sustained post-cache performance is claimed. Sustained-write testing is **NOT IMPLEMENTED** and Phase 6 limits were not expanded. Comparison now also requires the same policy version, filesystem and complete operation selection. Existing short-read completion loops and actual-completed-byte/tick denominators were retained.

Target mapping is resolved again after consent and before file creation. A mapping or target change invalidates consent and refuses the run. Ambiguous mappings remain filesystem-scoped and cannot enter physical-device comparison.

## Owned-file safety

Ownership records now bind the exact created file to Windows volume serial and file index. Creation rejects a target path or benchmark directory containing a reparse point. Every operation and cleanup revalidates the manifest, exact filename/path, no-reparse condition and live file identity. Cleanup revalidates immediately before deletion and never recursively deletes a directory. Corrupt manifests, redirected paths and files replaced after manifest creation cannot authorize deletion. Failure tests use GUID-named disposable `%TEMP%` directories.

## UI and runtime stability

The benchmark header and limitations are visible before expansion. Long status text wraps with a tooltip, history controls wrap, units remain explicit, and unavailable values remain labeled. Runtime captures passed at a 1366×768 window (1350×729 client DIPs) and 1800×1000 window (1784×961 client DIPs) on the actual 96-DPI host. Visible benchmark controls stayed inside the window; existing minimum-size virtualization, keyboard focus, repeated-operation gates, cancellation, selection and close protections passed.

Actual 125% and 150% scaling, mixed-DPI monitor movement and screen-reader testing were unavailable and are **NOT RUN**. The PerMonitorV2 manifest is present but does not substitute for those tests.

## Packaging

`build/release.ps1` stops on Release/UI verification failure, publishes Windows x64 self-contained, rejects source/tests/symbols/samples/secrets, invokes Inno Setup 6, and records SHA-256. `installer/pixinit.iss` installs per user under `%LocalAppData%\Programs\PIXINIT`, requests no elevation, supplies version/publisher/icon, Start menu and optional desktop shortcuts, and includes uninstall. Writable data remains under `%LocalAppData%\PIXINIT`; no uninstall directive removes it.

Installer: `artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe`  
SHA-256: `05E60AC831C3072078BB3E226935341FBBA7B42FA485DD6236C96D898768ED02`  
Signature: **NotSigned**; no self-signed certificate was created.

Silent per-user installation to a dedicated directory returned 0. The installed self-contained application remained running after startup without elevation. Silent uninstall returned 0, removed the installation directory, and left the pre-existing history database byte-for-byte unchanged (SHA-256 before/after matched). A clean external VM was unavailable, so cross-machine installation remains pending.

## Verification

- Release build: 0 warnings, 0 errors.
- Behavior suite: 226 passed.
- UI suite: 237 passed.
- Unique Phase 7 behavior checks: 9.
- Unique Phase 7 responsive UI checks: 3.
- Installer pipeline: build/test gate, self-contained publish, compile and SHA-256 passed.
- Installer install/launch/uninstall: passed on the current non-elevated Windows 10 user profile.
- Physical hardware suite: not rerun in Phase 7 because its harness includes real benchmark writes; last Phase 6 consent-approved total remains 253.

Screenshots: `screenshots/phase7-responsive-1366x768.png` and `screenshots/phase7-responsive-desktop.png`.

## Remaining blockers

Public distribution is blocked by missing code signing and the incomplete release matrix: physical SATA, Windows 11, other controllers/filesystems, clean VM installation, 125%/150% and mixed DPI, screen readers and broader accessibility. Sustained-write testing remains outside scope. The package must not be described as supporting all PCs or as broadly production-qualified.
