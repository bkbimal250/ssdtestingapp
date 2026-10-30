# Benchmark settings

The previous Custom/Advanced guide is superseded by the simplified Benchmark screen.

| Setting | Purpose |
| --- | --- |
| Passes | Repeat each selected read/write test. Results show the average of completed valid passes. |
| File size (MiB) | Temporary file size. Timed tests reuse it, so cumulative writes can be larger than occupied space. |
| Target volume | Select a mounted volume belonging to the selected physical disk. |
| All / Stop | Run all automatic profiles, or cancel the active run and clean up. |
| Profile button | Run only that profile using the same passes and file size. |

- SATA and NVMe profile choices are automatic; timed mode, queue depths and five-second windows are not user settings.
- Hover over Read/Write results for Best, IOPS and per-pass evidence.
- Health, temperature and host writes are in Diagnostics.
- See [current validation](simple-benchmark-validation.md) for defaults, write safety, tests and limitations.
