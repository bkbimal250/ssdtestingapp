# Media evidence and identity UI validation — 2026-10-07

Discovery previously never assigned StorageDevice.Media and appended a misleading “diagnostics not yet queried” statement even after diagnostic reads. Unknown therefore reflected missing classification, not a health fault or proof of denied permissions.

Retained the existing read-only seek-penalty query as a nullable structured field. No new hardware commands. Absence of seek penalty does not establish SSD media, and NVMe/SATA protocol is not a media classification. The UI now says Not confirmed and explains the available evidence, without fabricating a classification. Full serial remains displayed. Both protocol headers use compact wrapping protocol/media/access cards and keyboard-accessible expandable evidence; reported bus remains available in discovery details.

Release build: 0 warnings/errors. Full applicable suite: 255 unique checks (238 behavior + 17 UI), including retained media evidence, no inferred SSD, accurate wording and clearing selection. No physical benchmark or additional hardware diagnostic reads. WPF fixture screenshots regenerated; NVMe laptop and SATA desktop visually inspected. These are actual WPF empty-state test renders, not live hardware/installed-app screenshots. Laptop content scrolls. Installed launch and 125%/150% DPI: NOT RUN.

Screenshots: screenshots/post-phase7-nvme-1366x768.png and screenshots/sata-desktop.png.

Self-contained Windows x64 publish and Inno compilation succeeded. Installer: artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe. Unsigned; install/upgrade not exercised in this pass. SHA-256: 8A8F43CB4468127CDB7E82B212F9CB08BFD8DFB231629CB6AA9B2D81F43BDDB4.

Remaining: authoritative SSD/HDD media classification is not implemented for this discovery path; the displayed uncertainty is intentional. Live Intel hardware revalidation, physical SATA, mixed DPI and screen reader remain unverified. Existing safeguards/history/benchmark providers preserved. No GitHub publication performed.
