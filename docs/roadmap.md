# Seven-phase roadmap

Phases 1–7 are implemented within the verified scope below.

1. **UI and architecture foundation — complete.** Native WPF shell, distinct SATA/NVMe screens, honest unavailable states, protocol contracts, deterministic selection, runtime verification.
2. **Read-only discovery and selection integration — complete.** Present Windows disk interfaces, direct bus classification, unknown handling, session identities, conservative Windows-volume mapping, automatic discovery and safe rescans. Validated on the available direct NVMe disk; wider hardware coverage remains pending.
3. **SATA diagnostics — implementation complete.** Explicit read-only IDENTIFY and three SMART commands, pure parsers, strict allowlist and lifecycle integration. Vendor semantics remain unknown. Physical SATA validation NOT RUN; hardware support is unverified.
4. **NVMe diagnostics — implementation complete.** Read-only Windows protocol queries, controller/namespace parsers, SMART/Health counters, bounded Error Information, explicit refresh and actual INTEL NVMe validation. Namespace details remain unavailable until a trustworthy NSID mapping exists.
5. **Interpretation and history — complete.** Versioned protocol rules, diagnostic checklist, provenance-aware immutable SQLite snapshots, retention, conservative comparison and redacted TXT/JSON export. No invented health percentages.
6. **Reporting and performance — complete.** Consent-gated bounded filesystem-file sequential/random read/write measurement, monotonic throughput/IOPS/latency, owned-file cleanup, SQLite history/comparison, and TXT/JSON export. No raw-drive writes or universal scores.
7. **Release hardening and packaging — complete within current-machine scope.** Benchmark claims and owned-file identity were hardened, 1366×768/desktop layouts were captured, and a gated self-contained x64 per-user installer was compiled and exercised through install/launch/uninstall. Signing, physical SATA, Windows 11, other storage stacks, 125%/150% and mixed-DPI, screen readers and broader release qualification remain pending.

Release readiness remains **PARTIAL**. The package is reproducible and locally installable, but it is unsigned and the hardware, OS, filesystem, DPI and accessibility matrix is incomplete. See phase-7-validation.md.
