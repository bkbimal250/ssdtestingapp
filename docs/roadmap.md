# Seven-phase roadmap

Phases 1–5 are implemented. Phases 6–7 remain a proposed sequence, not completed product capabilities.

1. **UI and architecture foundation — complete.** Native WPF shell, distinct SATA/NVMe screens, honest unavailable states, protocol contracts, deterministic selection, runtime verification.
2. **Read-only discovery and selection integration — complete.** Present Windows disk interfaces, direct bus classification, unknown handling, session identities, conservative Windows-volume mapping, automatic discovery and safe rescans. Validated on the available direct NVMe disk; wider hardware coverage remains pending.
3. **SATA diagnostics — implementation complete.** Explicit read-only IDENTIFY and three SMART commands, pure parsers, strict allowlist and lifecycle integration. Vendor semantics remain unknown. Physical SATA validation NOT RUN; hardware support is unverified.
4. **NVMe diagnostics — implementation complete.** Read-only Windows protocol queries, controller/namespace parsers, SMART/Health counters, bounded Error Information, explicit refresh and actual INTEL NVMe validation. Namespace details remain unavailable until a trustworthy NSID mapping exists.
5. **Interpretation and history — complete.** Versioned protocol rules, diagnostic checklist, provenance-aware immutable SQLite snapshots, retention, conservative comparison and redacted TXT/JSON export. No invented health percentages.
6. **Reporting and performance scope.** Review exports, support workflows and benchmark requirements. Any benchmark capability needs a separately agreed scope; no write benchmark is authorized here.
7. **Release validation and packaging.** Hardware/controller matrix, accessibility, DPI and multiple monitors, installer, signing and release support policy.

Phase 6 scope-planning readiness: **READY**, but no benchmark implementation is authorized. Hardware-release readiness: **PARTIAL**. The available INTEL NVMe assessment/history/export path passed, while broader NVMe/controller/OS coverage and physical SATA validation remain pending. See phase-5-validation.md.
