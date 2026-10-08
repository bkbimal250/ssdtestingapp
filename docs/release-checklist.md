# PIXINIT release checklist

Status on 2026-10-08: **PARTIAL**. Current package evidence: [release validation](release-2026-10-08.md). Installation and DPI steps: [installation guide](installation.md).

## Required for every package

- [x] Release build succeeds with zero warnings and errors.
- [x] Behavior and UI verification passes before packaging.
- [x] Windows x64 self-contained publish is generated from `pixinit/pixinit.csproj`.
- [x] Publish output excludes tests, source, symbols, samples and secrets.
- [x] Manifest requests `requireAdministrator`; installer requests administrator consent and targets `{autopf}\PIXINIT` (2026-10-06 user requirement).
- [x] History/settings stay in `%LocalAppData%\PIXINIT` and are not removed by uninstall.
- [x] Installer provides Start menu entry, optional desktop shortcut and uninstall entry.
- [x] Installer SHA-256 is recorded in `artifacts/installer/SHA256SUMS.txt`.
- [ ] Revalidate elevated installation, launch, upgrade from the older per-user package and uninstall. Previous non-elevated verification does not qualify this changed permission model.
- [x] Installer and application signature status is reported honestly as **unsigned**.
- [ ] Code-sign application and installer with an organization-controlled certificate.
- [ ] Verify signed upgrade/downgrade behavior and publisher identity.

## Release qualification still required

- [ ] Physical SATA diagnostics and benchmark target mapping.
- [ ] Windows 11 and supported Windows servicing baselines.
- [ ] Additional NVMe controllers, multi-namespace devices and restricted-access stacks.
- [ ] ReFS, FAT/exFAT, removable/network/virtual/tiered storage and low-space disposable volumes.
- [ ] Actual 125%, 150%, mixed-DPI and multi-monitor movement.
- [ ] Screen-reader workflow and broader keyboard/accessibility audit.
- [ ] Installer test in a clean Windows VM and enterprise software-control environments.
- [ ] Define support, update, rollback and vulnerability-response policy.
- [x] Bounded sustained filesystem testing is implemented with separate limits and overall/post-drop reporting.
- [ ] Qualify real sustained workloads on additional hardware. SLC exhaustion and post-cache/NAND performance remain unproven.
