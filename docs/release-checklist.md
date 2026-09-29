# PIXINIT release checklist

Status on 2026-09-28 after the focused UI/data-presentation pass: **PARTIAL**.

## Required for every package

- [x] Release build succeeds with zero warnings and errors.
- [x] Behavior and UI verification passes before packaging.
- [x] Windows x64 self-contained publish is generated from `pixinit/pixinit.csproj`.
- [x] Publish output excludes tests, source, symbols, samples and secrets.
- [x] Manifest requests `asInvoker`; installer uses per-user `{localappdata}\Programs\PIXINIT`.
- [x] History/settings stay in `%LocalAppData%\PIXINIT` and are not removed by uninstall.
- [x] Installer provides Start menu entry, optional desktop shortcut and uninstall entry.
- [x] Installer SHA-256 is recorded in `artifacts/installer/SHA256SUMS.txt`.
- [x] Silent install, installed launch and silent uninstall pass in a dedicated directory.
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
- [ ] Sustained-write test design, if ever requested; currently **NOT IMPLEMENTED** and existing limits must not be expanded implicitly.
