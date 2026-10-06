# Full serial, permission and accuracy update — 2026-10-06

User-requested changes: show full device information and request administrator permissions.

- SATA/NVMe headers show the complete Windows-reported serial. ATA and NVMe Identify details show their own complete serial separately. No attempt is made to rewrite a genuine disagreement between sources.
- Diagnostic and benchmark export UI defaults to full serials; the explicit Redact serial option remains available. Existing historical/exported snapshots are not rewritten. Low-level report APIs retain their safe defaults; UI passes the chosen option.
- Identity comparison trims only outer NUL/space padding. Internal serial characters/spaces remain significant; regression coverage prevents accidentally hiding a mismatch.
- Serial text wraps, model text truncates with its full tooltip, metric values wrap, diagnostic buttons no longer stretch vertically, and empty SATA wording says No drive selected.
- Installer requests admin consent and targets Program Files. Application manifest requests requireAdministrator on launch. Windows may ask for consent on each launch; installation cannot grant permanent unrestricted access. History remains under the running user's LocalAppData. Supplying another administrator's credentials can select that administrator's profile; cross-account history migration is not implemented.
- No new protocol command, diagnostic read or benchmark run was introduced. Unsupported/Unavailable values, uncertain NVMe namespace scope and explicit diagnostic actions remain intact. Elevated access does not guarantee hardware support or measurement accuracy.

Validation: final Release build 0 warnings/0 errors; 246 total applicable checks passed (233 behavior checks plus 13 UI checks, not additive duplicate suites). New checks cover full SATA/discovery/Identify serial and preservation of internal serial differences. WPF fixture captures inspected; they are not physical-device verification. UAC approval/install/launch and upgrade from the older per-user installation are NOT RUN for this new privilege model. Physical SATA, additional controllers, 125%/150%/mixed DPI remain unverified. Package remains unsigned and release qualification PARTIAL.

Prior installer hashes and non-elevated launch results in older reports are historical. The current artifact hash is in artifacts/installer/SHA256SUMS.txt. No GitHub push or distribution performed in this update.

Final installer SHA-256: 87BE2465DFD3F2A6C40EB9760B6DFD733D23C8D3526B4BB7988D48A5185D20C5. Inno compilation succeeded with the final Program Files/admin configuration.
